module Fsxpired.Tests.DoctorTests

open System.IO
open NUnit.Framework
open FsUnit
open Fsxpired.Tests.TestHelpers

let private here = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)

/// The .NET version line changes with the runtime; the rest must not.
let private scrubRuntime (text: string) =
    text.Split('\n')
    |> Array.map (fun line ->
        if line.StartsWith "fsxpired 1.2.3, .NET" then
            "fsxpired 1.2.3, .NET {version}"
        else
            line
    )
    |> String.concat "\n"

let private files =
    [
        "build.fsx",
        """(* #r "nuget: Hidden, 1.0.0" *)
#r "nuget: Fun.Build, 1.1.18"
#r "nuget: Fantomas.Core, 8.0.0-beta-001"
#r "nuget: Fantomas.Core, 6.*"
#r "nuget: Humanizer.Core"
#r "nuget: Nobody.Knows, 1.0.0"
#r "System.Xml"
#load "helper.fsx"
#load "Types.fs"
#load "nowhere.fsx"
"""
        "helper.fsx", ""
        "Types.fs", "module Types"
    ]

[<Test>]
let ``doctor walks a script`` () =
    let result = run files world [ "doctor"; "build.fsx" ]
    result.ExitCode |> should equal 1
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor on a clean script exits 0`` () =
    let result =
        run [ "build.fsx", "#r \"nuget: Fun.Build, 1.1.20\"" ] world [ "doctor"; "build.fsx" ]

    result.ExitCode |> should equal 0
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor with the prerelease flag`` () =
    let result =
        run [ "build.fsx", "#r \"nuget: Fun.Build, 1.1.20\"" ] world [ "doctor"; "-pre"; "build.fsx" ]

    result.ExitCode |> should equal 0
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor reports an unsupported directive without stopping`` () =
    let result =
        run
            [
                "build.fsx", "#i \"nuget: https://example.org/v3/index.json\"\n#r \"nuget: Fun.Build, 1.1.18\""
            ]
            world
            [ "doctor"; "build.fsx" ]

    result.ExitCode |> should equal 1
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor refuses a folder`` () =
    let result = run files world [ "doctor"; "." ]
    result.ExitCode |> should equal 1
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor on a missing file`` () =
    let result = run files world [ "doctor"; "nowhere.fsx" ]
    result.ExitCode |> should equal 1
    result |> transcript |> scrubRuntime |> verify here

[<Test>]
let ``doctor when the feed is down`` () =
    let result = run files (BrokenFeed()) [ "doctor"; "build.fsx" ]
    result.ExitCode |> should equal 1
    result |> transcript |> scrubRuntime |> verify here
