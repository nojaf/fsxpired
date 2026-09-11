/// "Did you mean" for a mistyped flag or command.
module Fsxpired.Suggestion

open System

let editDistance (a: string) (b: string) : int =
    let d = Array2D.zeroCreate (a.Length + 1) (b.Length + 1)

    for i in 0 .. a.Length do
        d[i, 0] <- i

    for j in 0 .. b.Length do
        d[0, j] <- j

    for i in 1 .. a.Length do
        for j in 1 .. b.Length do
            let cost = if a[i - 1] = b[j - 1] then 0 else 1
            d[i, j] <- min (min (d[i - 1, j] + 1) (d[i, j - 1] + 1)) (d[i - 1, j - 1] + cost)

    d[a.Length, b.Length]

/// The closest candidate when it is close enough to be a typo rather than a different word.
let nearest (candidates: string list) (given: string) : string option =
    let given = given.ToLowerInvariant()

    candidates
    |> List.choose (fun c ->
        let distance = editDistance (c.ToLowerInvariant()) given

        if distance <= max 2 (given.Length / 3) then
            Some(c, distance)
        else
            None
    )
    |> List.sortBy snd
    |> List.tryHead
    |> Option.map fst
