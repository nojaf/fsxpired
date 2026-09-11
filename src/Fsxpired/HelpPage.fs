/// The help page, rendered from the same flag list the parser reads. Lines are Spectre markup:
/// the console decides whether the colours show, so a pipe gets plain text.
module Fsxpired.HelpPage

open Spectre.Console
open Fsxpired.Arguments

let private descriptionColumn = 22

// One accent, emerald, where Fantomas uses its blue. Spectre picks the nearest colour a four bit
// terminal can show, and drops the markup on a console without colours.
let private accent = "#50c878"

let private escape (text: string) = Markup.Escape text

let title (text: string) = $"[bold %s{accent}]%s{escape text}[/]"
let link (text: string) = $"[%s{accent}]%s{escape text}[/]"
let heading (text: string) = $"[bold]%s{escape text}[/]"
let flagName (text: string) = $"[bold %s{accent}]%s{escape text}[/]"
let placeholder (text: string) = $"[grey]%s{escape text}[/]"
let muted (text: string) = $"[dim]%s{escape text}[/]"

/// A row whose left column is padded to the description column. The plain text decides the
/// padding, the markup is what gets written.
let private padded (leftPlain: string) (leftMarkup: string) (lines: string list) =
    let indent = String.replicate descriptionColumn " "

    match lines with
    | [] -> [ $"  %s{leftMarkup}" ]
    | first :: rest ->
        let head =
            if leftPlain.Length + 2 >= descriptionColumn then
                [ $"  %s{leftMarkup}"; indent + escape first ]
            else
                let gap = String.replicate (descriptionColumn - leftPlain.Length - 2) " "
                [ $"  %s{leftMarkup}%s{gap}%s{escape first}" ]

        head @ (rest |> List.map (fun line -> indent + escape line))

let private flagLines (command: Command) =
    flags
    |> List.filter (fun f -> f.Commands |> List.contains command)
    |> List.collect (fun f ->
        let plain, markup =
            match f.Short with
            | Some short -> $"%s{short}, %s{f.Long}", $"%s{flagName short}, %s{flagName f.Long}"
            | None -> f.Long, flagName f.Long

        padded plain markup f.Description
    )

/// An example: the invocation muted, its arguments highlighted, the explanation at a fixed column.
let private example (invocation: string) (arguments: string) (explanation: string) =
    let plain = $"%s{invocation} %s{arguments}".TrimEnd()

    let markup =
        if System.String.IsNullOrEmpty arguments then
            muted invocation
        else
            $"%s{muted invocation} %s{flagName arguments}"

    let gap = String.replicate (max 1 (28 - plain.Length)) " "
    $"  %s{markup}%s{gap}%s{escape explanation}"

let private exitCode (code: int) (meaning: string) =
    $"  [bold]%d{code}[/]  %s{escape meaning}"

let render (invocation: string) (version: string) (command: Command) : string list =
    let versionText = muted $"(%s{version})"
    let usage = heading "Usage:"
    let tool = muted invocation
    let doctorName = flagName "doctor"
    let file = placeholder "<file>"
    let site = link "https://nojaf.github.io/fsxpired/"
    let repository = link "https://github.com/nojaf/fsxpired"

    match command with
    | Command.Outdated ->
        let name = title "fsxpired"
        let arguments = flagName "[command] [...flags] [...paths]"

        [
            $"%s{name} reports outdated #r \"nuget\" references in F# scripts. %s{versionText}"
            ""
            $"%s{usage} %s{tool} %s{arguments}"
            ""
            heading "Examples:"
            example invocation "" "every .fsx under the current folder"
            example invocation "build.fsx" "one script, and everything it #loads"
            example invocation "-u scripts" "rewrite outdated pins under scripts/"
            example invocation "--json | jq" "for scripts and agents"
            ""
            heading "Commands:"
            yield!
                padded
                    "doctor <file>"
                    $"%s{doctorName} %s{file}"
                    [
                        "Walk one script through every step and report what"
                        "happened at each: the directives found, the loads"
                        "resolved, whether nuget.org answered, and the verdict"
                        "for each reference with its reason."
                    ]
            ""
            heading "Flags:"
            yield! flagLines Command.Outdated
            ""
            heading "Paths:"
            "  Files and folders. A folder is searched recursively for .fsx files, skipping"
            "  bin, obj, node_modules, .git and whatever .gitignore excludes. No paths means"
            "  the current folder. A script's #load chain is followed."
            ""
            heading "Exit codes:"
            exitCode 0 "nothing outdated, or every outdated pin was rewritten"
            exitCode 1 "at least one reference is outdated"
            exitCode 2 "unexpected error: unreadable file, nuget.org unreachable, unsupported directive"
            exitCode 3 "a file could not be written"
            ""
            $"  %s{site}"
            $"  %s{repository}"
        ]
    | Command.Doctor ->
        let name = title "fsxpired doctor"
        let flagsPlaceholder = flagName "[...flags]"

        [
            $"%s{name} walks one script through every step and reports what happened. %s{versionText}"
            ""
            $"%s{usage} %s{tool} %s{doctorName} %s{flagsPlaceholder} %s{file}"
            ""
            heading "Flags:"
            yield! flagLines Command.Doctor
            ""
            heading "Paths:"
            "  One .fsx file. A folder is refused: the answers differ per file."
            ""
            heading "Exit codes:"
            exitCode 0 "every step ran"
            exitCode 1 "a step failed"
            exitCode 2 "the arguments were wrong"
        ]
