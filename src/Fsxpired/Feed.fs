namespace Fsxpired

open System
open System.Collections.Concurrent
open System.Threading
open NuGet.Common
open NuGet.Protocol
open NuGet.Protocol.Core.Types
open NuGet.Versioning

/// Where versions come from. The tool queries nuget.org; tests hand the resolver a fake.
type IFeed =
    abstract Name: string
    /// Every version the feed knows for the id, in any order. Empty when the feed does not know
    /// the package. Raises when the feed cannot be reached.
    abstract Versions: id: string -> Async<NuGetVersion list>
    /// Whether the feed answers at all, for `doctor`. `Ok` carries a short description of what
    /// answered; `Error` carries what went wrong.
    abstract Probe: unit -> Async<Result<string, string>>

/// nuget.org through NuGet.Protocol. One lookup per distinct id per instance, cached for the run.
type NuGetOrgFeed() =
    let source = "https://api.nuget.org/v3/index.json"
    let repository = Repository.Factory.GetCoreV3 source
    let cache = new SourceCacheContext()

    let results =
        ConcurrentDictionary<string, Async<NuGetVersion list>>(StringComparer.OrdinalIgnoreCase)

    let fetch (id: string) =
        async {
            let! ct = Async.CancellationToken

            let! resource =
                repository.GetResourceAsync<FindPackageByIdResource>(ct) |> Async.AwaitTask

            let! versions =
                resource.GetAllVersionsAsync(id, cache, NullLogger.Instance, ct)
                |> Async.AwaitTask

            return List.ofSeq versions
        }

    interface IFeed with
        member _.Name = "nuget.org"

        member _.Versions id =
            // Async.StartChild-free memoisation: the first caller's computation is cached as a
            // completed value once it finishes; concurrent callers of the same id share it.
            results.GetOrAdd(
                id,
                fun id ->
                    let task = Async.StartAsTask(fetch id, cancellationToken = CancellationToken.None)
                    Async.AwaitTask task
            )

        member _.Probe() =
            async {
                try
                    let! ct = Async.CancellationToken

                    let! index =
                        repository.GetResourceAsync<ServiceIndexResourceV3>(ct) |> Async.AwaitTask

                    let count = index.Entries.Count
                    return Ok $"service index at %s{source} answered with %d{count} resources"
                with ex ->
                    return Error $"%s{source}: %s{ex.Message}"
            }

    interface IDisposable with
        member _.Dispose() = cache.Dispose()
