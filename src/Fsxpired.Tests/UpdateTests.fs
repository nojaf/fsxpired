module Fsxpired.Tests.UpdateTests

open System.IO
open System.Text
open NUnit.Framework
open FsUnit
open Fsxpired.Tests.TestHelpers

let private here = Path.Combine(__SOURCE_DIRECTORY__, __SOURCE_FILE__)

let private read (run: Run) (path: string) =
    run.FileSystem.File.ReadAllText(run.FileSystem.Path.Combine(root, path))

let private buildScript =
    """#r "nuget: Fun.Build, 1.1.18"
#r   "nuget:Newtonsoft.Json,13.0.1"
#r @"nuget:   Humanizer.Core ,   2.14.1  "
#r "nuget: FSharp.Data, 6.3.0"
#r "nuget: Fantomas.Core, 6.*"
#r "nuget: Suave"
#load "helper.fsx"

printfn "%s" "#r \"nuget: Fun.Build, 1.1.18\""
"""

let private helperScript =
    "#r \"nuget: Fantomas.Core, 8.0.0-beta-001\"\r\n#r \"nuget: Suave, 2.6.0\"\r\nprintfn \"crlf\"\r\n"

[<Test>]
let ``update rewrites exact pins and reports them`` () =
    let result =
        run [ "build.fsx", buildScript; "helper.fsx", helperScript ] world [ "-u" ]

    result.ExitCode |> should equal 0

    [
        transcript result
        "--- build.fsx ---"
        read result "build.fsx"
        "--- helper.fsx ---"
        (read result "helper.fsx").Replace("\r\n", "<CRLF>\n")
    ]
    |> String.concat "\n"
    |> verify here

[<Test>]
let ``update in json`` () =
    let result =
        run [ "build.fsx", buildScript; "helper.fsx", helperScript ] world [ "-u"; "--json" ]

    result.ExitCode |> should equal 0
    result |> transcript |> verify here

[<Test>]
let ``update with the prerelease flag`` () =
    let result =
        run [ "build.fsx", "#r \"nuget: Fun.Build, 1.1.20\"" ] world [ "-u"; "-pre" ]

    result.ExitCode |> should equal 0
    read result "build.fsx" |> should equal "#r \"nuget: Fun.Build, 1.2.0-beta1\""

[<Test>]
let ``update leaves a current file untouched`` () =
    let files = [ "build.fsx", "#r \"nuget: FSharp.Data, 6.3.0\"" ]
    let result = run files world [ "-u" ]
    result.ExitCode |> should equal 0
    read result "build.fsx" |> should equal "#r \"nuget: FSharp.Data, 6.3.0\""

[<Test>]
let ``update keeps a byte order mark`` () =
    let fs = fileSystem []

    let bytes =
        Array.append (UTF8Encoding(true).GetPreamble()) (Encoding.UTF8.GetBytes "#r \"nuget: Fun.Build, 1.1.18\"\n")

    fs.AddFile(fs.Path.Combine(root, "build.fsx"), System.IO.Abstractions.TestingHelpers.MockFileData bytes)
    let result = runWith fs world [ "-u" ]
    result.ExitCode |> should equal 0
    let written = fs.File.ReadAllBytes(fs.Path.Combine(root, "build.fsx"))
    written[0..2] |> should equal (UTF8Encoding(true).GetPreamble())

    Encoding.UTF8.GetString(written, 3, written.Length - 3)
    |> should equal "#r \"nuget: Fun.Build, 1.1.20\"\n"

[<Test>]
let ``update does not write when a directive stops the run`` () =
    let files =
        [
            "build.fsx", "#r \"nuget: Fun.Build, 1.1.18\"\n#load \"docs.fsx\""
            "docs.fsx", "#r \"nuget: FSharp.Formatting,{{fsdocs-package-version}}\""
        ]

    let result = run files world [ "-u" ]
    result.ExitCode |> should equal 2

    read result "build.fsx"
    |> should equal "#r \"nuget: Fun.Build, 1.1.18\"\n#load \"docs.fsx\""

[<Test>]
let ``apply replaces spans from the end`` () =
    let text = "aaa bbb ccc"

    let edits =
        [
            ({
                Span = { Start = 0; Length = 3 }
                Replacement = "1"
            }
            : Fsxpired.Update.Edit)
            ({
                Span = { Start = 8; Length = 3 }
                Replacement = "33333"
            }
            : Fsxpired.Update.Edit)
        ]

    Fsxpired.Update.apply text edits |> should equal "1 bbb 33333"
