/// The table for people: one per file, then a summary line.
module Fsxpired.Report

open Spectre.Console
open Fsxpired.Resolve

type Summary =
    {
        Outdated: int
        Floating: int
        Unknown: int
        Updated: int
        Files: int
    }

let summarize (resolutions: Resolution list) : Summary =
    let count status =
        resolutions |> List.filter (fun r -> r.Status = status) |> List.length

    {
        Outdated = count Status.Outdated
        Floating = count Status.Floating
        Unknown = count Status.Unknown
        Updated = count Status.Updated
        Files =
            resolutions
            |> List.map (fun r -> r.Reference.File)
            |> List.distinct
            |> List.length
    }

let private plural n word =
    if n = 1 then $"%d{n} %s{word}" else $"%d{n} %s{word}s"

let summaryLine (summary: Summary) : string =
    let parts =
        [
            if summary.Updated > 0 then
                $"%d{summary.Updated} updated"
            $"%d{summary.Outdated} outdated"
            if summary.Floating > 0 then
                $"%d{summary.Floating} floating"
            if summary.Unknown > 0 then
                $"%d{summary.Unknown} unknown"
        ]

    let files = plural summary.Files "file"
    $"""%s{System.String.Join(", ", parts)} in %s{files}"""

let latestOf (versions: NuGet.Versioning.NuGetVersion list) =
    match versions with
    | [] -> None
    | _ -> Some(List.max versions)

let private version (v: NuGet.Versioning.NuGetVersion option) =
    match v with
    | Some v -> v.ToNormalizedString()
    | None -> "?"

/// The Current column: the pin as written, or the range as written with what it resolves to.
let currentText (resolution: Resolution) : string =
    match resolution.Kind, resolution.Reference.Version, resolution.Resolved with
    | Kind.Floating, _, _ -> ""
    | Kind.Exact _, Some text, _ -> text
    | Kind.Range _, Some text, Some resolved -> $"%s{text} (%s{resolved.ToNormalizedString()})"
    | Kind.Range _, Some text, None -> text
    | _, None, _ -> ""

let statusText (resolution: Resolution) : string =
    match resolution.Status with
    | Status.Outdated -> "outdated"
    | Status.Current -> "up to date"
    | Status.Floating -> "floating"
    | Status.Unknown -> "unknown"
    | Status.Updated ->
        let from = resolution.Reference.Version |> Option.defaultValue ""
        $"updated %s{from} -> %s{version resolution.Latest}"

let private statusMarkup (resolution: Resolution) : string =
    let text = Markup.Escape(statusText resolution)

    match resolution.Status with
    | Status.Outdated -> $"[red]%s{text}[/]"
    | Status.Current -> $"[green]%s{text}[/]"
    | Status.Floating
    | Status.Unknown -> $"[yellow]%s{text}[/]"
    | Status.Updated -> $"[green]%s{text}[/]"

/// Render the report for every file that holds references, in the order the files were met.
let render (console: IAnsiConsole) (relativePath: string -> string) (resolutions: Resolution list) : unit =
    let border =
        if console.Profile.Capabilities.Unicode then
            TableBorder.Rounded
        else
            TableBorder.Ascii

    let byFile = resolutions |> List.groupBy (fun r -> r.Reference.File)

    for file, rows in byFile do
        console.MarkupLine($"[bold]%s{Markup.Escape(relativePath file)}[/]")
        let hasNotes = rows |> List.exists (fun r -> Option.isSome r.Note)
        let table = Table()
        table.Border <- border
        table.AddColumn "Package" |> ignore
        table.AddColumn "Current" |> ignore
        table.AddColumn "Latest" |> ignore
        table.AddColumn "Status" |> ignore

        if hasNotes then
            table.AddColumn "Note" |> ignore

        for row in rows do
            let cells =
                [|
                    Markup.Escape row.Reference.Id
                    Markup.Escape(currentText row)
                    Markup.Escape(version row.Latest)
                    statusMarkup row
                    if hasNotes then
                        let note = Markup.Escape(row.Note |> Option.defaultValue "")
                        $"[grey]%s{note}[/]"
                |]

            table.AddRow cells |> ignore

        console.Write table
        console.WriteLine()

    if List.isEmpty resolutions then
        console.MarkupLine "no #r \"nuget\" references found"
    else
        console.MarkupLine(Markup.Escape(summaryLine (summarize resolutions)))
