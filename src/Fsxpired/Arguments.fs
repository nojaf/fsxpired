/// The command line, hand-rolled. The flag list below is the one source of truth: the parser reads
/// it, and so does the help page, so the page cannot describe a flag the parser refuses.
module Fsxpired.Arguments

open System

[<RequireQualifiedAccess; Struct>]
type Command =
    | Outdated
    | Doctor

type Flag =
    {
        Short: string option
        Long: string
        Description: string list
        Commands: Command list
    }

let flags: Flag list =
    [
        {
            Short = Some "-u"
            Long = "--update"
            Description = [ "Rewrite each outdated exact pin to the latest version it may move to." ]
            Commands = [ Command.Outdated ]
        }
        {
            Short = Some "-pre"
            Long = "--prerelease"
            Description = [ "Let stable pins move to, and be compared against, prereleases." ]
            Commands = [ Command.Outdated; Command.Doctor ]
        }
        {
            Short = None
            Long = "--json"
            Description = [ "Write the report as JSON to stdout, everything else to stderr." ]
            Commands = [ Command.Outdated ]
        }
        {
            Short = None
            Long = "--version"
            Description = [ "Print the version and exit." ]
            Commands = [ Command.Outdated; Command.Doctor ]
        }
        {
            Short = Some "-h"
            Long = "--help"
            Description = [ "Print this page and exit." ]
            Commands = [ Command.Outdated; Command.Doctor ]
        }
    ]

let commandName (command: Command) =
    match command with
    | Command.Outdated -> ""
    | Command.Doctor -> "doctor"

let commandNames = [ "doctor" ]

type Options =
    {
        Command: Command
        Update: bool
        Prerelease: bool
        Json: bool
        Paths: string list
    }

[<RequireQualifiedAccess>]
type Parsed =
    | Run of Options
    | Help of Command
    | Version
    | Refused of message: string

let private spellings (flag: Flag) =
    [ yield flag.Long; yield! Option.toList flag.Short ]

let private allSpellings = flags |> List.collect spellings

let private find (command: Command) (given: string) =
    flags
    |> List.tryFind (fun f -> spellings f |> List.contains given)
    |> Option.map (fun f -> f, f.Commands |> List.contains command)

let parse (invocation: string) (argv: string list) : Parsed =
    let command, rest =
        match argv with
        | "doctor" :: rest -> Command.Doctor, rest
        | _ -> Command.Outdated, argv

    let rec go (options: Options) (args: string list) =
        match args with
        | [] ->
            Parsed.Run
                { options with
                    Paths = List.rev options.Paths
                }
        | "--" :: paths ->
            Parsed.Run
                { options with
                    Paths = List.rev options.Paths @ paths
                }
        | arg :: rest when arg.StartsWith("-", StringComparison.Ordinal) ->
            match find command arg with
            | Some(flag, true) ->
                match flag.Long with
                | "--help" -> Parsed.Help command
                | "--version" -> Parsed.Version
                | "--update" -> go { options with Update = true } rest
                | "--prerelease" -> go { options with Prerelease = true } rest
                | "--json" -> go { options with Json = true } rest
                | other -> Parsed.Refused $"'%s{other}' is listed but not handled; this is a bug."
            | Some(flag, false) -> Parsed.Refused $"'%s{flag.Long}' does not apply to '%s{commandName command}'."
            | None ->
                let hint =
                    match Suggestion.nearest allSpellings arg with
                    | Some near -> $" Did you mean '%s{near}'?"
                    | None -> ""

                Parsed.Refused $"'%s{arg}' is not a fsxpired flag.%s{hint} See '%s{invocation} --help'."
        | arg :: rest when
            command = Command.Outdated
            && List.contains arg commandNames
            && List.isEmpty options.Paths
            ->
            ignore rest
            Parsed.Refused $"'%s{arg}' is a command and must come first: '%s{invocation} %s{arg} <file>'."
        | arg :: rest ->
            go
                { options with
                    Paths = arg :: options.Paths
                }
                rest

    match
        go
            {
                Command = command
                Update = false
                Prerelease = false
                Json = false
                Paths = []
            }
            rest
    with
    | Parsed.Run options when command = Command.Doctor && List.length options.Paths <> 1 ->
        Parsed.Refused $"'doctor' takes exactly one file: '%s{invocation} doctor <file>'."
    | Parsed.Run options when command = Command.Outdated && List.isEmpty options.Paths |> not ->
        match Suggestion.nearest commandNames (List.head options.Paths) with
        | Some near when not ((List.head options.Paths).Contains ".") ->
            Parsed.Refused $"'%s{List.head options.Paths}' is not a fsxpired command. Did you mean '%s{near}'?"
        | _ -> Parsed.Run options
    | parsed -> parsed
