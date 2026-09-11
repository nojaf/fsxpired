module Fsxpired.Tests.ArgumentsTests

open NUnit.Framework
open FsUnit
open Fsxpired.Arguments

let private parse args = parse "fsxpired" args

let private run args =
    match parse args with
    | Parsed.Run options -> options
    | other -> failwith $"expected a run, got {other}"

let private refused args =
    match parse args with
    | Parsed.Refused message -> message
    | other -> failwith $"expected a refusal, got {other}"

[<Test>]
let ``no arguments is the outdated command on the current folder`` () =
    let options = run []
    options.Command |> should equal Command.Outdated
    options.Paths |> should equal List.empty<string>

[<Test>]
let ``flags and paths mix in any order`` () =
    let options = run [ "build.fsx"; "-u"; "scripts"; "--prerelease"; "--json" ]
    options.Update |> should be True
    options.Prerelease |> should be True
    options.Json |> should be True
    options.Paths |> should equal [ "build.fsx"; "scripts" ]

[<Test>]
let ``short spellings`` () =
    let options = run [ "-u"; "-pre" ]
    options.Update |> should be True
    options.Prerelease |> should be True

[<Test>]
let ``help and version win`` () =
    parse [ "build.fsx"; "--help" ] |> should equal (Parsed.Help Command.Outdated)
    parse [ "-h" ] |> should equal (Parsed.Help Command.Outdated)
    parse [ "doctor"; "--help" ] |> should equal (Parsed.Help Command.Doctor)
    parse [ "--version" ] |> should equal Parsed.Version

[<Test>]
let ``unknown flag gets a suggestion`` () =
    refused [ "--updte" ]
    |> should equal "'--updte' is not a fsxpired flag. Did you mean '--update'? See 'fsxpired --help'."

[<Test>]
let ``unknown flag without a near miss`` () =
    refused [ "--frobnicate" ]
    |> should equal "'--frobnicate' is not a fsxpired flag. See 'fsxpired --help'."

[<Test>]
let ``doctor takes exactly one file`` () =
    (run [ "doctor"; "build.fsx" ]).Command |> should equal Command.Doctor

    refused [ "doctor" ]
    |> should equal "'doctor' takes exactly one file: 'fsxpired doctor <file>'."

    refused [ "doctor"; "a.fsx"; "b.fsx" ]
    |> should equal "'doctor' takes exactly one file: 'fsxpired doctor <file>'."

[<Test>]
let ``doctor refuses flags that do not apply`` () =
    refused [ "doctor"; "--json"; "build.fsx" ]
    |> should equal "'--json' does not apply to 'doctor'."

    refused [ "doctor"; "-u"; "build.fsx" ]
    |> should equal "'--update' does not apply to 'doctor'."

[<Test>]
let ``a command after a path is refused`` () =
    refused [ "doctr" ]
    |> should equal "'doctr' is not a fsxpired command. Did you mean 'doctor'?"

    refused [ "-u"; "doctor" ]
    |> should equal "'doctor' is a command and must come first: 'fsxpired doctor <file>'."

[<Test>]
let ``double dash ends the flags`` () =
    let options = run [ "--"; "-weird.fsx" ]
    options.Paths |> should equal [ "-weird.fsx" ]
