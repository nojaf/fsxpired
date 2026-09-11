module Fsxpired.Tests.TestHelpers

open System
open System.IO
open System.IO.Abstractions
open System.IO.Abstractions.TestingHelpers
open System.Threading.Tasks
open NuGet.Versioning
open NUnit.Framework
open Spectre.Console
open VerifyNUnit
open VerifyTests
open Fsxpired

/// A feed with a fixed version list per id. Ids are matched case-insensitively, as nuget.org does.
type FakeFeed(packages: (string * string list) list) =
    interface IFeed with
        member _.Name = "fake"

        member _.Versions id =
            async {
                return
                    packages
                    |> List.tryFind (fun (known, _) -> known.Equals(id, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (snd >> List.map NuGetVersion.Parse)
                    |> Option.defaultValue []
            }

        member _.Probe() =
            async { return Ok "fake feed answered" }

/// A feed that cannot be reached.
type BrokenFeed() =
    interface IFeed with
        member _.Name = "broken"

        member _.Versions _ =
            async { return failwith "connection refused" }

        member _.Probe() =
            async { return Error "connection refused" }

/// What nuget.org knows in every CLI test, so that the scenarios read against one fixed world.
let world =
    FakeFeed
        [
            "Fun.Build", [ "1.1.17"; "1.1.18"; "1.1.20"; "1.2.0-beta1" ]
            "FSharp.Data", [ "6.3.0" ]
            "Humanizer.Core", [ "2.14.1"; "3.0.10" ]
            "Fantomas.Core", [ "6.3.16"; "7.0.6"; "8.0.0-beta-001"; "8.0.0-beta-002" ]
            "Newtonsoft.Json", [ "13.0.1"; "13.0.4" ]
            "Suave", [ "2.6.0"; "2.7.0-beta1"; "2.7.0-beta2" ]
        ]

/// A root that reads the same on every platform: the mock file system maps it to its own drive.
let root =
    let fs = MockFileSystem()
    fs.Path.GetFullPath(fs.Path.Combine(fs.Directory.GetCurrentDirectory(), "repo"))

/// A mock file system holding the given files, paths relative to `root`.
let fileSystem (files: (string * string) list) : MockFileSystem =
    let fs = MockFileSystem()
    fs.Directory.CreateDirectory root |> ignore

    for path, content in files do
        fs.AddFile(fs.Path.Combine(root, path), MockFileData content)

    fs

[<NoComparison; NoEquality>]
type Run =
    {
        ExitCode: int
        Out: string
        Error: string
        FileSystem: IFileSystem
    }

let private console (out: TextWriter) =
    let settings =
        AnsiConsoleSettings(
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = AnsiConsoleOutput(out)
        )

    let console = AnsiConsole.Create settings
    console.Profile.Width <- 200
    console.Profile.Capabilities.Unicode <- true
    console

/// Run the tool in process against a file system and a feed.
let runWith (fs: IFileSystem) (feed: IFeed) (argv: string list) : Run =
    use out = new StringWriter()
    use error = new StringWriter()

    let env =
        {
            FileSystem = fs
            WorkingDirectory = root
            Out = out
            Error = error
            Console = console out
            Feed = feed
            Invocation = "fsxpired"
            Version = "1.2.3"
        }

    let exitCode = Cli.run env (Array.ofList argv)

    {
        ExitCode = exitCode
        Out = out.ToString()
        Error = error.ToString()
        FileSystem = fs
    }

let run (files: (string * string) list) (feed: IFeed) (argv: string list) : Run = runWith (fileSystem files) feed argv

/// The transcript of a run: exit code, stdout and stderr, with the root scrubbed so the snapshot
/// reads the same on every platform.
let transcript (run: Run) : string =
    let scrub (text: string) =
        text.Replace(root.Replace("\\", "\\\\"), "{root}").Replace(root, "{root}").Replace("{root}\\", "{root}/")

    String.Join(
        "\n",
        [
            $"exit code: {run.ExitCode}"
            ""
            "--- stdout ---"
            scrub run.Out
            "--- stderr ---"
            scrub run.Error
        ]
    )

/// The folder the sample scripts are copied to.
let testCases = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestCases")

let private settings (parameters: string option) =
    let settings = VerifySettings()
    settings.UseDirectory "snapshots"
    settings.DisableDiff()

    match parameters with
    | Some text -> settings.UseTextForParameters text
    | None -> ()

    settings

/// Snapshot a string. `sourceFile` is the test file the snapshot is named after.
let verify (sourceFile: string) (text: string) : Task =
    let text = text.Replace("\r\n", "\n")
    task { let! _ = Verifier.Verify(text, settings None, sourceFile = sourceFile) in () }

/// Snapshot a string from a parameterised test, named after `parameters` rather than the
/// arguments' string form.
let verifyNamed (sourceFile: string) (parameters: string) (text: string) : Task =
    let text = text.Replace("\r\n", "\n")
    task { let! _ = Verifier.Verify(text, settings (Some parameters), sourceFile = sourceFile) in () }
