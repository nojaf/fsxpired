/// Binds parse, resolve, update and report to the command line. `run` is what tests call.
module Fsxpired.Cli

open System
open System.Text
open Spectre.Console
open Fsxpired.Arguments
open Fsxpired.Resolve

[<Literal>]
let ExitNothingOutdated = 0

[<Literal>]
let ExitOutdated = 1

[<Literal>]
let ExitError = 2

[<Literal>]
let ExitWriteFailed = 3

let private problemMessage (relativePath: string -> string) (problem: Problem) =
    let at file line = $"%s{relativePath file}(%d{line})"

    match problem with
    | Problem.FeedDirective(file, line, text) ->
        $"%s{at file line}: %s{text}\n  fsxpired queries nuget.org only and does not support #i yet. If you need this, open an issue at https://github.com/nojaf/fsxpired/issues."
    | Problem.InvalidVersion(file, line, id, text) ->
        $"%s{at file line}: '%s{text}' is not a NuGet version for %s{id}.\n  fsxpired does not know what to compare it against. If this is a form FSI accepts, open an issue at https://github.com/nojaf/fsxpired/issues."
    | Problem.MissingLoad(file, line, target, resolved) ->
        $"%s{at file line}: #load \"%s{target}\" not found at %s{resolved}."
    | Problem.MissingInput path -> $"%s{path}: not found."
    | Problem.UnreadableFile(path, message) -> $"%s{relativePath path}: could not be read: %s{message}"

/// Read a file's bytes as text and remember whether it started with a UTF-8 byte order mark, so
/// that writing it back keeps it.
let private readForUpdate (env: CliEnvironment) (file: string) =
    let bytes = env.FileSystem.File.ReadAllBytes file

    let hasBom =
        bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy

    let text =
        UTF8Encoding(false).GetString(bytes, (if hasBom then 3 else 0), bytes.Length - (if hasBom then 3 else 0))

    text, hasBom

let private write (env: CliEnvironment) (file: string) (text: string) (hasBom: bool) =
    env.FileSystem.File.WriteAllText(file, text, UTF8Encoding(hasBom))

let private outdatedCommand (env: CliEnvironment) (options: Options) : int =
    let fs = env.FileSystem

    let relativePath (file: string) =
        fs.Path.GetRelativePath(env.WorkingDirectory, file).Replace('\\', '/')

    let command = if options.Update then "update" else "outdated"

    let finish (exitCode: int) (error: string option) (resolutions: Resolution list) =
        match error with
        | Some message -> env.Error.WriteLine message
        | None -> ()

        if options.Json then
            env.Out.Write(JsonReport.render command env.WorkingDirectory exitCode error relativePath resolutions)
        elif Option.isNone error then
            Report.render env.Console relativePath resolutions

        exitCode

    let fail (messages: string list) =
        finish ExitError (Some(String.Join("\n", messages))) []

    match Scripts.expandInputs fs env.WorkingDirectory options.Paths with
    | Error problems -> fail (problems |> List.map (problemMessage relativePath))
    | Ok roots ->
        let scripts = Scripts.collect fs roots
        let problems = scripts |> List.collect (fun s -> s.Problems)

        if not (List.isEmpty problems) then
            fail (problems |> List.map (problemMessage relativePath))
        else
            let references = scripts |> List.collect (fun s -> s.References)

            let resolved =
                try
                    Ok(Resolve.resolve env.Feed options.Prerelease references |> Async.RunSynchronously)
                with ex ->
                    Error $"nuget.org could not be queried: %s{ex.Message}"

            match resolved with
            | Error message -> fail [ message ]
            | Ok resolutions ->
                if not options.Update then
                    let exitCode =
                        if resolutions |> List.exists (fun r -> r.Status = Status.Outdated) then
                            ExitOutdated
                        else
                            ExitNothingOutdated

                    finish exitCode None resolutions
                else
                    let failures =
                        Update.plan resolutions
                        |> List.choose (fun (file, edits) ->
                            try
                                let text, hasBom = readForUpdate env file
                                write env file (Update.apply text edits) hasBom
                                None
                            with ex ->
                                Some $"%s{relativePath file}: could not be written: %s{ex.Message}"
                        )

                    match failures with
                    | [] -> finish ExitNothingOutdated None (resolutions |> List.map Update.markUpdated)
                    | failures -> fail failures

let private doctorCommand (env: CliEnvironment) (options: Options) : int =
    let lines, exitCode = Doctor.run env options.Prerelease (List.head options.Paths)

    for line in lines do
        env.Out.WriteLine line

    exitCode

let run (env: CliEnvironment) (argv: string array) : int =
    match Arguments.parse env.Invocation (List.ofArray argv) with
    | Parsed.Help command ->
        for line in HelpPage.render env.Invocation env.Version command do
            env.Console.MarkupLine line

        ExitNothingOutdated
    | Parsed.Version ->
        env.Out.WriteLine env.Version
        ExitNothingOutdated
    | Parsed.Refused message ->
        env.Error.WriteLine message
        ExitError
    | Parsed.Run options ->
        match options.Command with
        | Command.Outdated -> outdatedCommand env options
        | Command.Doctor -> doctorCommand env options
