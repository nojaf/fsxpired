module Fsxpired.Tests.CliTests

open System.IO
open NUnit.Framework
open FsUnit
open Fsxpired.Tests.TestHelpers

let private here = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)

let private repo =
    [
        "build.fsx",
        """#r "nuget: Fun.Build, 1.1.18"
#r "nuget: FSharp.Data, 6.3.0"
#r "nuget: Humanizer.Core"
#load "scripts/helper.fsx"
#load "scripts/Types.fs"
"""
        "scripts/helper.fsx",
        """#r "nuget: Fantomas.Core, 8.0.0-beta-001"
#r "nuget: Fantomas.Core, 6.*"
#r "nuget: Nobody.Knows, 1.0.0"
#r "nuget: Suave, 2.6.0"
"""
        "scripts/Types.fs", "module Types"
        "scripts/plain.fsx", "printfn \"no references\""
        "bin/generated.fsx", "#r \"nuget: Fun.Build, 1.0.0\""
        "ignored/old.fsx", "#r \"nuget: Fun.Build, 1.0.0\""
        ".gitignore", "ignored/\n"
    ]

[<Test>]
let ``table for the whole folder`` () =
    run repo world [] |> transcript |> verify here

[<Test>]
let ``json for the whole folder`` () =
    run repo world [ "--json" ] |> transcript |> verify here

[<Test>]
let ``one file and its loads`` () =
    let result = run repo world [ "build.fsx" ]
    result.ExitCode |> should equal 1
    result |> transcript |> verify here

[<Test>]
let ``prerelease flag lets stable pins see prereleases`` () =
    run repo world [ "-pre"; "build.fsx" ] |> transcript |> verify here

[<Test>]
let ``nothing outdated exits 0`` () =
    let result = run [ "build.fsx", "#r \"nuget: FSharp.Data, 6.3.0\"" ] world []

    result.ExitCode |> should equal 0
    result |> transcript |> verify here

[<Test>]
let ``script without references`` () =
    let result = run [ "plain.fsx", "printfn \"hi\"" ] world []
    result.ExitCode |> should equal 0
    result |> transcript |> verify here

[<Test>]
let ``feed directive stops the run`` () =
    let result =
        run
            [
                "build.fsx", "#i \"nuget: https://example.org/v3/index.json\"\n#r \"nuget: Fun.Build, 1.1.18\""
            ]
            world
            []

    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``feed directive stops the run in json too`` () =
    let result =
        run [ "build.fsx", "#i \"nuget: https://example.org/v3/index.json\"" ] world [ "--json" ]

    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``placeholder version stops the run`` () =
    let result =
        run
            [
                "docs/page.fsx", "#r \"nuget: FSharp.Formatting,{{fsdocs-package-version}}\""
            ]
            world
            []

    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``missing load stops the run`` () =
    let result =
        run [ "build.fsx", "#load \"nowhere.fsx\"\n#r \"nuget: Fun.Build, 1.1.18\"" ] world []

    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``missing input stops the run`` () =
    let result = run repo world [ "nowhere.fsx"; "build.fsx" ]
    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``unreachable feed stops the run`` () =
    let result = run repo (BrokenFeed()) [ "build.fsx" ]
    result.ExitCode |> should equal 2
    result |> transcript |> verify here

[<Test>]
let ``help page`` () =
    let result = run [] world [ "--help" ]
    result.ExitCode |> should equal 0
    result |> transcript |> verify here

[<Test>]
let ``doctor help page`` () =
    run [] world [ "doctor"; "--help" ] |> transcript |> verify here

[<Test>]
let ``version`` () =
    let result = run [] world [ "--version" ]
    result.Out.Trim() |> should equal "1.2.3"

[<Test>]
let ``refused arguments exit 2`` () =
    let result = run [] world [ "--updte" ]
    result.ExitCode |> should equal 2
    result |> transcript |> verify here
