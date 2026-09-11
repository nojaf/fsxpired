namespace Fsxpired

open System.IO
open System.IO.Abstractions
open Spectre.Console

/// Everything the command line touches that a test wants to replace: the file system, the working
/// directory, the two output streams, the console that renders tables, and the feed.
[<NoComparison; NoEquality>]
type CliEnvironment =
    {
        FileSystem: IFileSystem
        /// Absolute. Relative paths on the command line and in the report are taken from here.
        WorkingDirectory: string
        /// The report goes here and nothing else does.
        Out: TextWriter
        /// Everything that is not the report.
        Error: TextWriter
        /// Renders to `Out`. Colours and box drawing are its business, decided from the terminal.
        Console: IAnsiConsole
        Feed: IFeed
        /// How the tool was started, `fsxpired` or `dotnet fsxpired`, so that examples in messages
        /// can be pasted.
        Invocation: string
        Version: string
    }
