/// Walk one script through every step and say what happened at each. The verbose mode.
module Fsxpired.Doctor

open System
open NuGet.Versioning
open Fsxpired.Resolve

let private version (v: NuGetVersion option) =
    match v with
    | Some v -> v.ToNormalizedString()
    | None -> "none"

let private problemText (problem: Problem) =
    match problem with
    | Problem.FeedDirective(_, _, text) -> $"unsupported: %s{text} (only nuget.org is queried)"
    | Problem.InvalidVersion(_, _, id, text) -> $"unsupported: '%s{text}' is not a NuGet version for %s{id}"
    | Problem.MissingLoad(_, _, target, resolved) -> $"missing: #load \"%s{target}\" resolves to %s{resolved}"
    | Problem.MissingInput path -> $"not found: %s{path}"
    | Problem.UnreadableFile(path, message) -> $"unreadable: %s{path}: %s{message}"

/// The report lines and the exit code: 1 when a step failed, 0 otherwise.
let run (env: CliEnvironment) (includePrerelease: bool) (input: string) : string list * int =
    let fs = env.FileSystem
    let path = fs.Path.GetFullPath(fs.Path.Combine(env.WorkingDirectory, input))
    let lines = ResizeArray<string>()
    let mutable failed = false

    let step (title: string) (body: string list) =
        lines.Add $"%s{title}"

        for line in body do
            lines.Add $"  %s{line}"

        lines.Add ""

    lines.Add $"fsxpired %s{env.Version}, .NET %A{Environment.Version}"
    lines.Add $"working directory: %s{env.WorkingDirectory}"
    lines.Add ""

    if fs.Directory.Exists path then
        step "1. File" [ $"%s{path}"; "a folder; doctor takes one file" ]
        failed <- true
    elif not (fs.File.Exists path) then
        step "1. File" [ $"%s{path}"; "not found" ]
        failed <- true
    elif not (fs.Path.GetExtension(path).Equals(".fsx", StringComparison.OrdinalIgnoreCase)) then
        step "1. File" [ $"%s{path}"; "not a .fsx script" ]
        failed <- true
    else
        let text = fs.File.ReadAllText path
        let lineCount = text.Split('\n').Length
        let lineWord = if lineCount = 1 then "line" else "lines"
        step "1. File" [ $"%s{path}"; $"%d{lineCount} %s{lineWord}" ]

        let parsed = Parse.parseScript path text

        step
            "2. Parse"
            [
                match parsed.Diagnostics with
                | [] -> "parsed cleanly"
                | diagnostics ->
                    "the F# parser reported:"

                    for d in diagnostics do
                        $"  %s{d}"
                match parsed.Directives with
                | [] -> "no hash directives"
                | directives ->
                    for d in directives do
                        let handling =
                            match d.Handling with
                            | Handling.NuGetReference -> "nuget reference"
                            | Handling.Load -> "load"
                            | Handling.Ignored reason -> $"ignored: %s{reason}"
                            | Handling.Problem problem ->
                                failed <- true
                                problemText problem

                        $"line %d{d.Line}: %s{d.Text}"
                        $"  %s{handling}"
            ]

        step
            "3. Loads"
            [
                match Scripts.resolveLoads fs parsed with
                | [] -> "no #load directives"
                | loads ->
                    for load, resolved, exists in loads do
                        let state =
                            if not exists then
                                failed <- true
                                "missing"
                            elif resolved.EndsWith(".fsx", StringComparison.OrdinalIgnoreCase) then
                                "followed"
                            else
                                "skipped, not a .fsx"

                        $"line %d{load.Line}: %s{load.Target} -> %s{resolved} (%s{state})"
            ]

        let probe = env.Feed.Probe() |> Async.RunSynchronously

        step
            "4. Feed"
            [
                match probe with
                | Ok answer -> answer
                | Error message ->
                    failed <- true
                    $"failed: %s{message}"
            ]

        match probe with
        | Error _ -> ()
        | Ok _ ->
            step
                "5. References"
                [
                    match parsed.References with
                    | [] -> "none"
                    | references ->
                        let resolutions =
                            Resolve.resolve env.Feed includePrerelease references |> Async.RunSynchronously

                        for r in resolutions do
                            let versions = env.Feed.Versions r.Reference.Id |> Async.RunSynchronously

                            let latestStable =
                                versions |> List.filter (fun v -> not v.IsPrerelease) |> Report.latestOf

                            let latestPrerelease =
                                versions |> List.filter (fun v -> v.IsPrerelease) |> Report.latestOf

                            let written = r.Reference.Version |> Option.defaultValue "(none)"
                            $"%s{r.Reference.Id} (line %d{r.Reference.Line}, written as %s{written})"
                            $"  %d{List.length versions} versions on nuget.org"
                            $"  latest stable %s{version latestStable}, latest prerelease %s{version latestPrerelease}"
                            $"  resolves to %s{version r.Resolved}, may move to %s{version r.Latest}"

                            let scope =
                                match r.Kind with
                                | Kind.Exact pin when pin.IsPrerelease -> "prereleases included because the pin is one"
                                | _ when includePrerelease -> "prereleases included by flag"
                                | _ -> "stable versions only"

                            let reason =
                                match r.Kind, r.Status with
                                | Kind.Floating, _ -> "no version pinned, so there is nothing to be behind"
                                | Kind.Range range, _ ->
                                    $"range %s{range.OriginalString} is a deliberate choice and is left alone"
                                | Kind.Exact _, Status.Outdated -> $"%s{version r.Latest} is newer, %s{scope}"
                                | Kind.Exact _, Status.Current -> $"nothing newer, %s{scope}"
                                | Kind.Exact _, Status.Unknown -> "nuget.org does not know the package"
                                | Kind.Exact _, Status.Updated -> "rewritten"
                                | Kind.Exact _, Status.Floating -> "no version pinned"

                            $"  verdict: %s{Report.statusText r}, %s{reason}"

                            match r.Note with
                            | Some note -> $"  note: %s{note}"
                            | None -> ()
                ]

    List.ofSeq lines, (if failed then 1 else 0)
