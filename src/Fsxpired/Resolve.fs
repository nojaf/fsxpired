/// References in, one lookup per distinct id, verdicts out.
module Fsxpired.Resolve

open System
open NuGet.Versioning

[<RequireQualifiedAccess; Struct>]
type Status =
    | Outdated
    | Current
    | Floating
    | Unknown
    | Updated

/// How the version text was read.
[<RequireQualifiedAccess; NoComparison>]
type Kind =
    /// A plain version such as `1.2.3`: the one FSI resolves to, and the one `-u` rewrites.
    | Exact of NuGetVersion
    /// Brackets or a wildcard: a deliberate choice, reported and left alone.
    | Range of VersionRange
    /// No version at all: resolves to latest on every run.
    | Floating

[<NoComparison>]
type Resolution =
    {
        Reference: Reference
        Kind: Kind
        /// What FSI resolves the reference to today, `None` when the feed does not know the id.
        Resolved: NuGetVersion option
        /// The newest version the reference may move to under the run's prerelease rule.
        Latest: NuGetVersion option
        Status: Status
        Note: string option
    }

let kind (reference: Reference) : Kind =
    match reference.Version with
    | None -> Kind.Floating
    | Some text ->
        let mutable exact: NuGetVersion = null

        if NuGetVersion.TryParse(text, &exact) then
            Kind.Exact exact
        else
            Kind.Range(VersionRange.Parse text)

/// The versions a reference may move to: stable ones, unless the run or the reference itself
/// opted into prereleases.
let private candidates (includePrerelease: bool) (kind: Kind) (versions: NuGetVersion list) =
    let optedIn =
        includePrerelease
        || (
            match kind with
            | Kind.Exact v -> v.IsPrerelease
            | Kind.Range r -> (r.HasLowerBound && r.MinVersion.IsPrerelease)
            | Kind.Floating -> false
        )

    if optedIn then
        versions
    else
        versions |> List.filter (fun v -> not v.IsPrerelease)

let private latestOf (versions: NuGetVersion list) =
    match versions with
    | [] -> None
    | _ -> Some(List.max versions)

/// The verdict for one reference given every version the feed knows for its id.
let verdict (includePrerelease: bool) (versions: NuGetVersion list) (reference: Reference) : Resolution =
    let kind = kind reference
    let latest = latestOf (candidates includePrerelease kind versions)

    let make resolved status note =
        {
            Reference = reference
            Kind = kind
            Resolved = resolved
            Latest = latest
            Status = status
            Note = note
        }

    match versions with
    | [] -> make None Status.Unknown (Some "not found on nuget.org")
    | _ ->
        match kind with
        | Kind.Floating -> make latest Status.Floating (Some "no version pinned, resolves to latest on every run")
        | Kind.Exact pin ->
            let known = versions |> List.contains pin
            let note = if known then None else Some $"%A{pin} is not on nuget.org"

            match latest with
            | Some newest when newest > pin -> make (Some pin) Status.Outdated note
            | _ -> make (Some pin) Status.Current note
        | Kind.Range range ->
            let resolved = range.FindBestMatch versions |> Option.ofObj

            match resolved with
            | None -> make None Status.Unknown (Some $"no version on nuget.org satisfies %s{range.OriginalString}")
            | Some _ -> make resolved Status.Current None

/// One lookup per distinct id, then a verdict per reference in the order given.
let resolve (feed: IFeed) (includePrerelease: bool) (references: Reference list) : Async<Resolution list> =
    async {
        let ids =
            references
            |> List.map (fun r -> r.Id)
            |> List.distinctBy (fun id -> id.ToLowerInvariant())

        let! fetched =
            ids
            |> List.map (fun id ->
                async {
                    let! versions = feed.Versions id
                    return id, versions
                }
            )
            |> Async.Parallel

        let byId =
            Collections.Generic.Dictionary<string, NuGetVersion list>(StringComparer.OrdinalIgnoreCase)

        for id, versions in fetched do
            byId[id] <- versions

        return references |> List.map (fun r -> verdict includePrerelease byId[r.Id] r)
    }
