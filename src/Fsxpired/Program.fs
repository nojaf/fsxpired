module Fsxpired.Program

open System
open System.IO
open System.IO.Abstractions
open System.Reflection
open Spectre.Console

let private version () =
    let assembly = Assembly.GetExecutingAssembly()

    let informational =
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun a -> a.InformationalVersion)
        |> Option.defaultWith (fun () -> string<Version>(assembly.GetName().Version))

    // Reproducible builds append the full commit hash; nine characters are enough to find it.
    match informational.IndexOf '+' with
    | -1 -> informational
    | plus when informational.Length > plus + 10 -> informational.Substring(0, plus + 10)
    | _ -> informational

/// `dotnet fsxpired` when started through the dotnet host, as a local tool is, `fsxpired` when
/// started through the shim a global tool installs.
let private invocation () =
    match Environment.ProcessPath |> Option.ofObj with
    | Some path when Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ->
        "dotnet fsxpired"
    | _ -> "fsxpired"

let private console () =
    let redirected = Console.IsOutputRedirected

    let settings =
        AnsiConsoleSettings(
            Ansi = (if redirected then AnsiSupport.No else AnsiSupport.Detect),
            ColorSystem =
                (if redirected then
                     ColorSystemSupport.NoColors
                 else
                     ColorSystemSupport.Detect),
            Out = AnsiConsoleOutput(Console.Out)
        )

    let console = AnsiConsole.Create settings

    // A pipe or a file gets plain ASCII and a wide table; a terminal gets what it can show.
    if redirected then
        console.Profile.Width <- 160
        console.Profile.Capabilities.Unicode <- false

    console

[<EntryPoint>]
let main argv =
    use feed = new NuGetOrgFeed()

    let env =
        {
            FileSystem = FileSystem()
            WorkingDirectory = Directory.GetCurrentDirectory()
            Out = Console.Out
            Error = Console.Error
            Console = console ()
            Feed = feed
            Invocation = invocation ()
            Version = version ()
        }

    Cli.run env argv
