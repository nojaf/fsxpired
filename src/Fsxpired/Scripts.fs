/// Which scripts a run looks at: the paths on the command line expanded, and every `#load` chain
/// followed. Each file is parsed once, however many times it is reached.
module Fsxpired.Scripts

open System
open System.IO.Abstractions

/// Folders no walk enters, whatever `.gitignore` says.
let private skippedFolders = set [ "bin"; "obj"; "node_modules"; ".git" ]

let private isScript (fs: IFileSystem) (path: string) =
    fs.Path.GetExtension(path).Equals(".fsx", StringComparison.OrdinalIgnoreCase)

/// The `.gitignore` files that apply to a folder, nearest last, each with the folder it is
/// relative to.
type private IgnoreScope = (Ignore.Ignore * string) list

let private isIgnored (fs: IFileSystem) (scope: IgnoreScope) (path: string) (isDirectory: bool) =
    scope
    |> List.exists (fun (ignore, root) ->
        let relative = fs.Path.GetRelativePath(root, path).Replace('\\', '/')
        let relative = if isDirectory then relative + "/" else relative
        ignore.IsIgnored relative
    )

let private withGitIgnore (fs: IFileSystem) (scope: IgnoreScope) (folder: string) : IgnoreScope =
    let file = fs.Path.Combine(folder, ".gitignore")

    if fs.File.Exists file then
        let rules = Ignore.Ignore()
        rules.Add(fs.File.ReadAllLines file) |> ignore
        scope @ [ rules, folder ]
    else
        scope

/// Every `.fsx` under a folder, sorted, skipping the fixed folders and whatever `.gitignore`
/// excludes.
let rec private walk (fs: IFileSystem) (scope: IgnoreScope) (folder: string) : string list =
    let scope = withGitIgnore fs scope folder

    let files =
        fs.Directory.GetFiles folder
        |> Array.filter (fun f -> isScript fs f && not (isIgnored fs scope f false))
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> List.ofArray

    let folders =
        fs.Directory.GetDirectories folder
        |> Array.filter (fun d ->
            not (skippedFolders.Contains(fs.Path.GetFileName d))
            && not (isIgnored fs scope d true)
        )
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> List.ofArray

    files @ (folders |> List.collect (walk fs scope))

/// The scripts the command line names: files as given, folders walked. Paths come back absolute.
let expandInputs
    (fs: IFileSystem)
    (workingDirectory: string)
    (inputs: string list)
    : Result<string list, Problem list>
    =
    let inputs = if List.isEmpty inputs then [ "." ] else inputs

    let expanded =
        inputs
        |> List.map (fun input ->
            let full = fs.Path.GetFullPath(fs.Path.Combine(workingDirectory, input))

            if fs.File.Exists full then Ok [ full ]
            elif fs.Directory.Exists full then Ok(walk fs [] full)
            else Error(Problem.MissingInput input)
        )

    let errors =
        expanded
        |> List.choose (
            function
            | Error p -> Some p
            | Ok _ -> None
        )

    if List.isEmpty errors then
        Ok(
            expanded
            |> List.collect (
                function
                | Ok files -> files
                | Error _ -> []
            )
            |> List.distinct
        )
    else
        Error errors

/// Parse every root and everything they `#load`, each file once, roots first and loads in the
/// order they are met. A `.fs` load is skipped: it cannot hold `#r`.
let collect (fs: IFileSystem) (roots: string list) : ScriptParse list =
    let seen = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
    let results = ResizeArray<ScriptParse>()

    let rec visit (file: string) =
        if seen.Add file then
            let parsed =
                try
                    Parse.parseScript file (fs.File.ReadAllText file)
                with ex ->
                    {
                        File = file
                        References = []
                        Loads = []
                        Problems = [ Problem.UnreadableFile(file, ex.Message) ]
                        Directives = []
                        Diagnostics = []
                    }

            let loadProblems, targets =
                parsed.Loads
                |> List.map (fun load ->
                    let folder = fs.Path.GetDirectoryName file
                    let resolved = fs.Path.GetFullPath(fs.Path.Combine(folder, load.Target))

                    if not (fs.File.Exists resolved) then
                        Error(Problem.MissingLoad(load.File, load.Line, load.Target, resolved))
                    elif isScript fs resolved then
                        Ok(Some resolved)
                    else
                        Ok None
                )
                |> List.partition Result.isError

            results.Add
                { parsed with
                    Problems =
                        parsed.Problems
                        @ (loadProblems
                           |> List.choose (
                               function
                               | Error p -> Some p
                               | Ok _ -> None
                           ))
                }

            targets
            |> List.iter (
                function
                | Ok(Some target) -> visit target
                | _ -> ()
            )

    roots |> List.iter visit
    List.ofSeq results

/// The load targets of one script resolved against its folder, for `doctor`.
let resolveLoads (fs: IFileSystem) (parsed: ScriptParse) : (LoadDirective * string * bool) list =
    let folder = fs.Path.GetDirectoryName parsed.File

    parsed.Loads
    |> List.map (fun load ->
        let resolved = fs.Path.GetFullPath(fs.Path.Combine(folder, load.Target))
        load, resolved, fs.File.Exists resolved
    )
