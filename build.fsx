#!/usr/bin/env -S dotnet fsi --
#r "nuget: Fun.Build, 1.1.18"
#r "nuget: Ionide.KeepAChangelog, 0.2.0"

open System
open System.Diagnostics
open System.IO
open Fun.Build
open Ionide.KeepAChangelog
open Ionide.KeepAChangelog.Domain
open SemVersion

let (</>) a b = Path.Combine(a, b)
let root = __SOURCE_DIRECTORY__
let packagesDir = root </> "artifacts" </> "package" </> "release"
let toolProject = "src/Fsxpired/Fsxpired.fsproj"

/// What Fantomas formats and the analyzers read.
let sources = "src build.fsx docs"

let isCI = not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "CI"))
let jsonFlagDuringCI = if isCI then "--json" else String.Empty

/// Whether this run was asked not to publish anything.
let isDryRun = fsi.CommandLineArgs |> Array.contains "--dry-run"

/// Run a program from the repository root with its output captured, and return the exit code,
/// standard output and standard error.
let runCaptured (program: string) (arguments: string list) : int * string * string =
    let startInfo =
        ProcessStartInfo(
            program,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        )

    for argument in arguments do
        startInfo.ArgumentList.Add argument

    use proc = Process.Start startInfo
    let output = proc.StandardOutput.ReadToEndAsync()
    let error = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()
    proc.ExitCode, output.Result, error.Result

pipeline "Build" {
    workingDir root
    stage "RestoreTools" { run "dotnet tool restore" }
    stage "CheckFormat" { run $"dotnet fantomas check {sources} {jsonFlagDuringCI}" }
    stage "Build" { run "dotnet build -c Release --tl" }
    stage "Test" { run "dotnet test -c Release --no-build --tl" }
    stage "Pack" { run $"dotnet pack {toolProject} -c Release --no-build --tl" }
    stage "Docs" {
        envVars
            [
                "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "1"
                "DOTNET_ROLL_FORWARD", "LatestMajor"
            ]
        run "dotnet fsdocs build --clean --strict --noapidocs --properties Configuration=Release"
    }
    runIfOnlySpecified false
}

pipeline "FormatAll" {
    workingDir root
    stage "RestoreTools" { run "dotnet tool restore" }
    stage "Format" { run $"dotnet fantomas {sources} {jsonFlagDuringCI}" }
    runIfOnlySpecified true
}

/// Everything on the command line after the pipeline name, so that `-p Docs --port 9000` reaches
/// fsdocs as is.
let extraArgs (pipelineName: string) : string =
    fsi.CommandLineArgs
    |> Array.skipWhile (fun arg -> arg <> pipelineName)
    |> Array.skip 1
    |> String.concat " "

pipeline "Docs" {
    workingDir root
    stage "RestoreTools" { run "dotnet tool restore" }
    stage "Watch" {
        envVars
            [
                "DOTNET_ROLL_FORWARD_TO_PRERELEASE", "1"
                "DOTNET_ROLL_FORWARD", "LatestMajor"
            ]
        run (fun ctx ->
            let arguments = extraArgs "Docs"
            ctx.RunCommand $"dotnet fsdocs watch --noapidocs %s{arguments}")
    }
    runIfOnlySpecified true
}

/// Where the analyzer packages were restored to. MSBuild knows, so the version is kept in one
/// place: Directory.Packages.props.
let analyzerPaths () : string list =
    let exitCode, output, error =
        runCaptured
            "dotnet"
            [
                "msbuild"
                toolProject
                "-getProperty:PkgIonide_Analyzers"
                "-getProperty:PkgG-Research_FSharp_Analyzers"
            ]

    if exitCode <> 0 then
        failwith $"Could not resolve the analyzer packages. Run `dotnet restore` first.\n{error}"

    let json = Text.Json.JsonDocument.Parse output

    [
        for property in json.RootElement.GetProperty("Properties").EnumerateObject() do
            let path = property.Value.GetString()

            if not (String.IsNullOrWhiteSpace path) then
                path </> "analyzers" </> "dotnet" </> "fs"
    ]

pipeline "Analyze" {
    workingDir root
    stage "RestoreTools" { run "dotnet tool restore" }
    stage "Restore" { run "dotnet restore" }
    stage "Analyze" {
        run (fun ctx ->
            async {
                let paths =
                    analyzerPaths ()
                    |> List.map (fun p -> $"--analyzers-path \"{p}\"")
                    |> String.concat " "

                match!
                    ctx.RunCommand
                        $"dotnet fsharp-analyzers --project {toolProject} {paths} --code-root {root} --report analysis.sarif --exclude-files **/obj/**"
                with
                | Ok() -> return 0
                | Error _ -> return 1
            })
    }
    runIfOnlySpecified true
}

/// The version and notes of the top entry of the changelog: what a push to main would release.
let currentRelease () =
    let changelog = FileInfo(root </> "CHANGELOG.md")

    match Parser.parseChangeLog changelog with
    | Error error -> failwith $"CHANGELOG.md could not be parsed: {error}"
    | Ok changelog ->
        match changelog.Releases with
        | [] -> failwith "CHANGELOG.md has no release entry."
        | (version: SemanticVersion, _date, data) :: _ ->
            let version = string version

            let notes =
                match data with
                | None -> ""
                | Some data ->
                    [
                        "Added", data.Added
                        "Changed", data.Changed
                        "Fixed", data.Fixed
                        "Deprecated", data.Deprecated
                        "Removed", data.Removed
                        "Security", data.Security
                        yield! Map.toList data.Custom
                    ]
                    |> List.choose (fun (header: string, body: string) ->
                        if String.IsNullOrWhiteSpace body then
                            None
                        else
                            Some $"### %s{header}\n%s{body.Trim()}")
                    |> String.concat "\n\n"

            version, notes

/// Whether GitHub already has a release for the version.
let releaseExists (version: string) =
    let exitCode, _, _ = runCaptured "gh" [ "release"; "view"; $"v{version}" ]
    exitCode = 0

pipeline "Release" {
    workingDir root
    stage "Build" { run "dotnet build -c Release --tl" }
    stage "Test" { run "dotnet test -c Release --no-build --tl" }
    stage "Pack" { run $"dotnet pack {toolProject} -c Release --no-build --tl" }
    stage "Release" {
        run (fun ctx ->
            async {
                let version, notes = currentRelease ()

                if releaseExists version then
                    printfn $"Release v{version} already exists on GitHub. Nothing to do."
                    return 0
                else
                    let nupkg =
                        Directory.GetFiles(packagesDir, $"fsxpired.{version}.nupkg") |> Array.exactlyOne

                    let isPrerelease = version.Contains "-"

                    if isDryRun then
                        printfn
                            $"[DRY-RUN] Would push {nupkg} and create release v{version} (prerelease: {isPrerelease})"
                        printfn $"[DRY-RUN] Notes:\n{notes}"
                        return 0
                    else
                        let key = Environment.GetEnvironmentVariable "NUGET_KEY"

                        match!
                            ctx.RunSensitiveCommand
                                $"dotnet nuget push \"{nupkg}\" --api-key \"{key}\" --source https://api.nuget.org/v3/index.json"
                        with
                        | Error _ -> return 1
                        | Ok() ->
                            let notesFile = root </> "artifacts" </> "release-notes.md"
                            File.WriteAllText(notesFile, notes)
                            let prerelease = if isPrerelease then "--prerelease" else ""

                            match!
                                ctx.RunCommand
                                    $"gh release create v{version} \"{nupkg}\" --title v{version} --notes-file \"{notesFile}\" {prerelease}"
                            with
                            | Ok() -> return 0
                            | Error _ -> return 1
            })
    }
    runIfOnlySpecified true
}

tryPrintPipelineCommandHelp ()
