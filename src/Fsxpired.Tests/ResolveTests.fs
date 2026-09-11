module Fsxpired.Tests.ResolveTests

open NUnit.Framework
open FsUnit
open NuGet.Versioning
open Fsxpired
open Fsxpired.Resolve

let private reference (version: string option) =
    {
        File = "/repo/build.fsx"
        Line = 1
        Id = "Pkg"
        Version = version
        VersionSpan = None
    }

let private versions (list: string list) = list |> List.map NuGetVersion.Parse

let private v (text: string) = Some(NuGetVersion.Parse text)

[<Test>]
let ``exact pin behind a stable release is outdated`` () =
    let r = verdict false (versions [ "1.0.0"; "1.0.1" ]) (reference (Some "1.0.0"))
    r.Status |> should equal Status.Outdated
    r.Latest |> should equal (v "1.0.1")
    r.Resolved |> should equal (v "1.0.0")

[<Test>]
let ``exact pin on the latest stable is current`` () =
    let r = verdict false (versions [ "1.0.0"; "1.0.1" ]) (reference (Some "1.0.1"))
    r.Status |> should equal Status.Current

[<Test>]
let ``stable pin ignores a newer prerelease`` () =
    let r =
        verdict false (versions [ "1.0.0"; "1.1.0-beta" ]) (reference (Some "1.0.0"))

    r.Status |> should equal Status.Current
    r.Latest |> should equal (v "1.0.0")

[<Test>]
let ``stable pin sees a newer prerelease with the flag`` () =
    let r = verdict true (versions [ "1.0.0"; "1.1.0-beta" ]) (reference (Some "1.0.0"))
    r.Status |> should equal Status.Outdated
    r.Latest |> should equal (v "1.1.0-beta")

[<Test>]
let ``prerelease pin moves to the next prerelease without the flag`` () =
    let r =
        verdict false (versions [ "1.0.0-beta1"; "1.0.0-beta2" ]) (reference (Some "1.0.0-beta1"))

    r.Status |> should equal Status.Outdated
    r.Latest |> should equal (v "1.0.0-beta2")

[<Test>]
let ``prerelease pin moves to the stable release once it exists`` () =
    let r =
        verdict false (versions [ "1.0.0-beta1"; "1.0.0" ]) (reference (Some "1.0.0-beta1"))

    r.Status |> should equal Status.Outdated
    r.Latest |> should equal (v "1.0.0")

[<Test>]
let ``wildcard range is current and shows what it resolves to`` () =
    let r = verdict false (versions [ "1.5.0"; "2.0.0" ]) (reference (Some "1.*"))
    r.Status |> should equal Status.Current
    r.Resolved |> should equal (v "1.5.0")
    r.Latest |> should equal (v "2.0.0")

[<Test>]
let ``bracket range resolves to its lowest match`` () =
    let r =
        verdict false (versions [ "1.0.0"; "1.5.0"; "2.0.0" ]) (reference (Some "[1.0,2.0)"))

    r.Status |> should equal Status.Current
    r.Resolved |> should equal (v "1.0.0")

[<Test>]
let ``range nothing satisfies is unknown`` () =
    let r = verdict false (versions [ "1.0.0" ]) (reference (Some "[3.0,4.0)"))
    r.Status |> should equal Status.Unknown

[<Test>]
let ``floating reference is floating and resolves to latest`` () =
    let r = verdict false (versions [ "1.0.0"; "1.0.1"; "2.0.0-rc1" ]) (reference None)
    r.Status |> should equal Status.Floating
    r.Resolved |> should equal (v "1.0.1")

[<Test>]
let ``package the feed does not know is unknown`` () =
    let r = verdict false [] (reference (Some "1.0.0"))
    r.Status |> should equal Status.Unknown
    r.Note |> should equal (Some "not found on nuget.org")

[<Test>]
let ``pin the feed does not know gets a note`` () =
    let r = verdict false (versions [ "1.0.0"; "1.0.1" ]) (reference (Some "1.0.5"))
    r.Status |> should equal Status.Current
    r.Note |> should equal (Some "1.0.5 is not on nuget.org")

[<Test>]
let ``each id is looked up once`` () =
    let mutable lookups = []

    let feed =
        { new IFeed with
            member _.Name = "counting"

            member _.Versions id =
                async {
                    lookups <- id :: lookups
                    return versions [ "1.0.0" ]
                }

            member _.Probe() = async { return Ok "" }
        }

    let references =
        [
            { reference (Some "1.0.0") with
                Id = "Fun.Build"
            }
            { reference (Some "1.0.0") with
                Id = "fun.build"
            }
            { reference None with Id = "Fun.Build" }
        ]

    resolve feed false references |> Async.RunSynchronously |> ignore
    lookups |> List.length |> should equal 1
