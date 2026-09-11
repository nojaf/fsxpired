module Fsxpired.Tests.ParseTests

open System
open System.IO
open System.IO.Abstractions
open NUnit.Framework
open Fsxpired
open Fsxpired.Tests.TestHelpers

let private here = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)

let private relative (path: string) =
    Path.GetRelativePath(testCases, path).Replace('\\', '/')

let private problemText (problem: Problem) =
    match problem with
    | Problem.FeedDirective(file, line, text) -> $"feed directive {relative file}:{line} {text}"
    | Problem.InvalidVersion(file, line, id, text) -> $"invalid version {relative file}:{line} {id} '{text}'"
    | Problem.MissingLoad(file, line, target, _) -> $"missing load {relative file}:{line} {target}"
    | Problem.MissingInput path -> $"missing input {path}"
    | Problem.UnreadableFile(path, message) -> $"unreadable {relative path}: {message}"

/// The parse as text: every reference with its version span checked against the source, then
/// the loads, problems and how each directive was handled.
let private describe (text: string) (parsed: ScriptParse) =
    [
        $"file: {relative parsed.File}"
        "references:"
        for r in parsed.References do
            let version =
                match r.Version, r.VersionSpan with
                | Some v, Some span ->
                    let slice = text.Substring(span.Start, span.Length)
                    let check = if slice = v then "span ok" else $"SPAN MISMATCH '{slice}'"
                    $"'{v}' at {span.Start}+{span.Length} ({check})"
                | _ -> "(floating)"

            $"  line {r.Line}: {r.Id} {version}"
        "loads:"
        for l in parsed.Loads do
            $"  line {l.Line}: {l.Target}"
        "problems:"
        for p in parsed.Problems do
            $"  {problemText p}"
        "directives:"
        for d in parsed.Directives do
            let handling =
                match d.Handling with
                | Handling.NuGetReference -> "nuget reference"
                | Handling.Load -> "load"
                | Handling.Ignored reason -> $"ignored: {reason}"
                | Handling.Problem p -> $"problem: {problemText p}"

            $"  line {d.Line}: {d.Text}"
            $"    {handling}"
        "diagnostics:"
        for d in parsed.Diagnostics do
            $"  {d}"
    ]
    |> String.concat "\n"

let sampleScripts () =
    Directory.GetFiles(testCases, "*.fsx")
    |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
    |> Array.map (fun path -> TestCaseData(path).SetName($"parse {Path.GetFileName path}"))

[<TestCaseSource(nameof sampleScripts)>]
let ``sample script`` (path: string) =
    let text = File.ReadAllText path
    let parsed = Parse.parseScript path text
    verifyNamed here (Path.GetFileNameWithoutExtension path) (describe text parsed)

[<Test>]
let ``load chain is followed once per file`` () =
    let fs = FileSystem()
    let root = Path.Combine(testCases, "chain", "root.fsx")
    let scripts = Scripts.collect fs [ root ]

    scripts
    |> List.map (fun s -> describe (File.ReadAllText s.File) s)
    |> String.concat "\n\n"
    |> verify here

[<Test>]
let ``missing load is a problem`` () =
    let fs = FileSystem()
    let root = Path.Combine(testCases, "chain", "missing.fsx")
    let scripts = Scripts.collect fs [ root ]

    scripts
    |> List.map (fun s -> describe (File.ReadAllText s.File) s)
    |> String.concat "\n\n"
    |> verify here

[<Test>]
let ``a file given twice is parsed once`` () =
    let fs = FileSystem()
    let root = Path.Combine(testCases, "chain", "root.fsx")
    let helper = Path.Combine(testCases, "chain", "shared", "helper.fsx")
    let scripts = Scripts.collect fs [ root; helper ]
    let files = scripts |> List.map (fun s -> relative s.File)
    Assert.That(files, Is.EqualTo<string list>([ "chain/root.fsx"; "chain/shared/helper.fsx"; "chain/other.fsx" ]))
