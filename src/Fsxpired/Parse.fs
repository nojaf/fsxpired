/// Script text in, references out. Uses the F# parser from Fantomas.FCS so that comments, strings
/// and spacing are handled exactly as FSI handles them.
module Fsxpired.Parse

open System
open Fantomas.FCS
open Fantomas.FCS.Syntax
open Fantomas.FCS.Text
open NuGet.Versioning

/// The scripts are parsed the way FSI sees them.
let private defines = [ "INTERACTIVE" ]

/// Absolute offset of the start of each line, so that a (line, column) from the parser becomes a
/// span in the text.
let private lineStarts (text: string) =
    let starts = ResizeArray [ 0 ]

    for i in 0 .. text.Length - 1 do
        if text[i] = '\n' then
            starts.Add(i + 1)

    starts.ToArray()

let private offset (starts: int array) (position: Position) =
    starts[position.Line - 1] + position.Column

/// The string literals a hash directive carries, with the span of each literal including its
/// quotes, in source order.
let private stringArguments (starts: int array) (args: ParsedHashDirectiveArgument list) =
    args
    |> List.choose (fun arg ->
        match arg with
        | ParsedHashDirectiveArgument.String(value, _, range) ->
            let start = offset starts range.Start
            let finish = offset starts range.End

            Some(
                value,
                {
                    Start = start
                    Length = finish - start
                }
            )
        | _ -> None
    )

/// Every hash directive in the tree, nested modules included, in source order.
let private hashDirectives (ast: ParsedInput) =
    let rec fromDecls (decls: SynModuleDecl list) =
        decls
        |> List.collect (fun decl ->
            match decl with
            | SynModuleDecl.HashDirective(directive, _) -> [ directive ]
            | SynModuleDecl.NestedModule(decls = inner) -> fromDecls inner
            | _ -> []
        )

    match ast with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        modules
        |> List.collect (fun (SynModuleOrNamespace(decls = decls)) -> fromDecls decls)
    | ParsedInput.SigFile _ -> []

/// `nuget: Id, Version` split into its parts. `None` when the text is not a nuget reference.
let private nugetParts (value: string) =
    let trimmed = value.TrimStart()

    if not (trimmed.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase)) then
        None
    else
        let rest = trimmed.Substring("nuget:".Length)

        match rest.IndexOf ',' with
        | -1 -> Some(rest.Trim(), None)
        | comma ->
            let id = rest.Substring(0, comma).Trim()
            let version = rest.Substring(comma + 1).Trim()
            Some(id, (if String.IsNullOrEmpty version then None else Some version))

/// The span of the version text inside a string literal's span, found by searching the source
/// slice of the literal so that whatever spacing the author used is left alone.
let private versionSpan (text: string) (literal: Span) (version: string) =
    let slice = text.Substring(literal.Start, literal.Length)
    let index = slice.LastIndexOf(version, StringComparison.Ordinal)

    {
        Start = literal.Start + index
        Length = version.Length
    }

let private canParseVersion (version: string) =
    let mutable range = null
    VersionRange.TryParse(version, &range)

/// Parse one script. `file` is the absolute path the references are attributed to; the text is
/// what it holds.
let parseScript (file: string) (text: string) : ScriptParse =
    let ast, diagnostics = Parse.parseFile false (SourceText.ofString text) defines
    let starts = lineStarts text

    let directives =
        hashDirectives ast
        |> List.map (fun (ParsedHashDirective(ident, args, range)) ->
            let line = range.StartLine
            let strings = stringArguments starts args

            let sourceText =
                let start = offset starts range.Start
                let finish = offset starts range.End
                text.Substring(start, finish - start)

            let handling =
                match ident, strings with
                | "r", [ (value, _) ] ->
                    match nugetParts value with
                    | None when value.TrimStart().StartsWith("paket:", StringComparison.OrdinalIgnoreCase) ->
                        Handling.Ignored "a Paket reference; use 'paket outdated'"
                    | None -> Handling.Ignored "not a nuget reference"
                    | Some(_, None) -> Handling.NuGetReference
                    | Some(_, Some version) when canParseVersion version -> Handling.NuGetReference
                    | Some(id, Some version) -> Handling.Problem(Problem.InvalidVersion(file, line, id, version))
                | "r", _ -> Handling.Ignored "not a single string argument"
                | "load", _ -> Handling.Load
                | "i", _ -> Handling.Problem(Problem.FeedDirective(file, line, sourceText))
                | _ -> Handling.Ignored $"#%s{ident} is not a reference"

            (ident, strings, line, sourceText, handling)
        )

    let references =
        directives
        |> List.choose (fun (ident, strings, line, _, handling) ->
            match ident, strings, handling with
            | "r", [ (value, literal) ], Handling.NuGetReference ->
                match nugetParts value with
                | Some(id, None) ->
                    Some
                        {
                            File = file
                            Line = line
                            Id = id
                            Version = None
                            VersionSpan = None
                        }
                | Some(id, Some version) ->
                    Some
                        {
                            File = file
                            Line = line
                            Id = id
                            Version = Some version
                            VersionSpan = Some(versionSpan text literal version)
                        }
                | None -> None
            | _ -> None
        )

    let loads =
        directives
        |> List.collect (fun (_, strings, line, _, handling) ->
            match handling with
            | Handling.Load ->
                strings
                |> List.map (fun (target, _) ->
                    {
                        File = file
                        Line = line
                        Target = target
                    }
                )
            | _ -> []
        )

    let problems =
        directives
        |> List.choose (fun (_, _, _, _, handling) ->
            match handling with
            | Handling.Problem problem -> Some problem
            | _ -> None
        )

    {
        File = file
        References = references
        Loads = loads
        Problems = problems
        Directives =
            directives
            |> List.map (fun (_, _, line, sourceText, handling) ->
                {
                    Line = line
                    Text = sourceText
                    Handling = handling
                }
            )
        Diagnostics = diagnostics |> List.map (fun d -> d.Message)
    }
