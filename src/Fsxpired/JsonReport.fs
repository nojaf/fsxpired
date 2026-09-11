/// The report for scripts and agents. One document on stdout, everything else on stderr.
///
/// The shape is not a contract: a reader that needs a promise wants the exit code. It mirrors the
/// table, with the fields a script needs to act on a reference.
module Fsxpired.JsonReport

open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open Fsxpired.Resolve

let private status (s: Status) =
    match s with
    | Status.Outdated -> "outdated"
    | Status.Current -> "current"
    | Status.Floating -> "floating"
    | Status.Unknown -> "unknown"
    | Status.Updated -> "updated"

let private versionOrNull (json: Utf8JsonWriter) (name: string) (v: NuGet.Versioning.NuGetVersion option) =
    match v with
    | Some v -> json.WriteString(name, v.ToNormalizedString())
    | None -> json.WriteNull name

let private stringOrNull (json: Utf8JsonWriter) (name: string) (v: string option) =
    match v with
    | Some v -> json.WriteString(name, v)
    | None -> json.WriteNull name

/// The document as text. `error` is always present, `null` when the run got to a report.
let render
    (command: string)
    (workingDirectory: string)
    (exitCode: int)
    (error: string option)
    (relativePath: string -> string)
    (resolutions: Resolution list)
    : string
    =
    use stream = new MemoryStream()

    let options =
        JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use json = new Utf8JsonWriter(stream, options)
    json.WriteStartObject()
    json.WriteString("command", command)
    json.WriteString("workingDirectory", workingDirectory)
    json.WriteNumber("exitCode", exitCode)
    stringOrNull json "error" error

    let summary = Report.summarize resolutions
    json.WriteStartObject "summary"
    json.WriteNumber("outdated", summary.Outdated)
    json.WriteNumber("floating", summary.Floating)
    json.WriteNumber("unknown", summary.Unknown)
    json.WriteNumber("updated", summary.Updated)
    json.WriteNumber("files", summary.Files)
    json.WriteEndObject()

    json.WriteStartArray "files"

    for file, rows in resolutions |> List.groupBy (fun r -> r.Reference.File) do
        json.WriteStartObject()
        json.WriteString("path", relativePath file)
        json.WriteStartArray "references"

        for row in rows do
            json.WriteStartObject()
            json.WriteString("id", row.Reference.Id)
            json.WriteNumber("line", row.Reference.Line)
            stringOrNull json "version" row.Reference.Version
            versionOrNull json "resolved" row.Resolved
            versionOrNull json "latest" row.Latest
            json.WriteString("status", status row.Status)
            stringOrNull json "note" row.Note
            json.WriteEndObject()

        json.WriteEndArray()
        json.WriteEndObject()

    json.WriteEndArray()
    json.WriteEndObject()
    json.Flush()
    Encoding.UTF8.GetString(stream.ToArray())
