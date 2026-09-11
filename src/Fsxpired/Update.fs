/// Rewriting version texts in place. Only the recorded span of each version changes; whitespace,
/// quoting and the rest of the file stay as written.
module Fsxpired.Update

open Fsxpired.Resolve

type Edit = { Span: Span; Replacement: string }

/// The edits a run would make: one per outdated exact pin, grouped by file.
let plan (resolutions: Resolution list) : (string * Edit list) list =
    resolutions
    |> List.choose (fun r ->
        match r.Status, r.Kind, r.Latest, r.Reference.VersionSpan with
        | Status.Outdated, Kind.Exact _, Some latest, Some span ->
            Some(
                r.Reference.File,
                {
                    Span = span
                    Replacement = latest.ToNormalizedString()
                }
            )
        | _ -> None
    )
    |> List.groupBy fst
    |> List.map (fun (file, edits) -> file, edits |> List.map snd)

/// Apply edits to a text. Later spans first, so earlier offsets stay valid.
let apply (text: string) (edits: Edit list) : string =
    edits
    |> List.sortByDescending (fun e -> e.Span.Start)
    |> List.fold
        (fun (current: string) edit ->
            current.Substring(0, edit.Span.Start)
            + edit.Replacement
            + current.Substring(edit.Span.Start + edit.Span.Length)
        )
        text

/// The resolution as it reads after its edit was written.
let markUpdated (resolution: Resolution) : Resolution =
    match resolution.Status, resolution.Kind with
    | Status.Outdated, Kind.Exact _ ->
        { resolution with
            Status = Status.Updated
        }
    | _ -> resolution
