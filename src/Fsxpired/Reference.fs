namespace Fsxpired

/// A character span in a file: the absolute offset of the first character and the length.
/// Recorded so that an update is a text replacement of exactly this span and nothing else.
[<Struct>]
type Span = { Start: int; Length: int }

/// One `#r "nuget: Id, Version"` directive as written in a script.
type Reference =
    {
        /// Absolute path of the file that holds the directive.
        File: string
        /// One-based.
        Line: int
        Id: string
        /// The version text as written, `None` for a floating reference such as `#r "nuget: Id"`.
        Version: string option
        /// The span of the version text in the file, `None` when there is no version.
        VersionSpan: Span option
    }

/// A `#load "path"` directive as written, before the path is resolved.
type LoadDirective =
    {
        File: string
        Line: int
        Target: string
    }

/// Something a script says that the tool cannot honour. Each one stops the run: continuing would
/// give a wrong answer for that script, and a wrong "up to date" is worse than none.
[<RequireQualifiedAccess>]
type Problem =
    /// `#i "nuget: <url>"`: an extra feed. Only nuget.org is queried.
    | FeedDirective of file: string * line: int * text: string
    /// A version text `NuGet.Versioning` cannot parse, such as a template placeholder.
    | InvalidVersion of file: string * line: int * id: string * text: string
    /// A `#load` target that is not there.
    | MissingLoad of file: string * line: int * target: string * resolved: string
    /// A file or folder given on the command line that is not there.
    | MissingInput of path: string
    /// A file that could not be read.
    | UnreadableFile of path: string * message: string

/// How a hash directive was handled. Kept for `doctor`, which reports every directive it met.
[<RequireQualifiedAccess>]
type Handling =
    | NuGetReference
    | Load
    | Ignored of reason: string
    | Problem of Problem

/// Every hash directive found in a script, in source order.
type Directive =
    {
        Line: int
        Text: string
        Handling: Handling
    }

/// What parsing one script produced.
type ScriptParse =
    {
        /// Absolute path.
        File: string
        References: Reference list
        Loads: LoadDirective list
        Problems: Problem list
        Directives: Directive list
        /// Messages from the F# parser, when the file does not parse cleanly. Parsing continues on
        /// what could be read; these are reported by `doctor` only.
        Diagnostics: string list
    }
