---
title: fsxpired
category: Documentation
categoryindex: 1
index: 1
---

# fsxpired

Reports which `#r "nuget: ..."` references in your F# scripts have a newer version on nuget.org,
and updates them when asked.

## Install

```shell
dotnet tool install -g fsxpired
```

Or as a local tool in a repository that has a `dotnet-tools.json`:

```shell
dotnet tool install fsxpired
dotnet fsxpired
```

## Use

```shell
fsxpired                 # every .fsx under the current folder
fsxpired build.fsx       # one script, and everything it #loads
fsxpired scripts docs    # files and folders, mixed
fsxpired -u              # rewrite outdated exact pins in place
fsxpired -pre            # let stable pins move to prereleases
fsxpired --json          # one JSON document on stdout, for scripts and agents
fsxpired doctor build.fsx  # walk one script through every step and say what happened
```

A folder is searched recursively for `.fsx` files, skipping `bin`, `obj`, `node_modules`, `.git`
and whatever `.gitignore` excludes. Each script's `#load` chain is followed, so a helper that holds
the references is reported under its own name, once.

## What the report says

One table per file, then a summary line:

```text
build.fsx
╭────────────────┬─────────┬────────┬────────────┬────────────────────────────────────────────────────╮
│ Package        │ Current │ Latest │ Status     │ Note                                               │
├────────────────┼─────────┼────────┼────────────┼────────────────────────────────────────────────────┤
│ Fun.Build      │ 1.1.18  │ 1.1.20 │ outdated   │                                                    │
│ FSharp.Data    │ 6.3.0   │ 6.3.0  │ up to date │                                                    │
│ Humanizer.Core │         │ 3.0.10 │ floating   │ no version pinned, resolves to latest on every run │
╰────────────────┴─────────┴────────┴────────────┴────────────────────────────────────────────────────╯

1 outdated, 1 floating in 1 file
```

The Note column only appears when a row has something to say.

- **outdated**: a newer version exists that the pin may move to. `-u` rewrites it.
- **up to date**: nothing newer, under the prerelease rule of the run.
- **floating**: no version pinned, so it resolves to the latest at every run. Shown in yellow,
  never counted, never rewritten.
- **unknown**: nuget.org does not know the package, or nothing satisfies the range.
- **updated**: rewritten by this run.

A range or wildcard such as `6.*` or `[1.0,2.0)` is a deliberate choice: it is shown with what it
resolves to today, reported as up to date, and left alone by `-u`.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Nothing outdated, or every outdated pin was rewritten |
| 1 | At least one reference is outdated |
| 2 | Unexpected error: unreadable file, missing `#load` target, nuget.org unreachable, or a directive fsxpired does not support |
| 3 | `-u` could not write a file |

## What stops a run

fsxpired stops with exit 2 rather than guess:

- `#i "nuget: <url>"`: only nuget.org is queried. A script that adds a feed would get wrong
  answers, and a wrong "up to date" is worse than none.
- A version text that is not a NuGet version, such as the `{{fsdocs-package-version}}`
  placeholder fsdocs substitutes in literate scripts.

If you need either, [open an issue](https://github.com/nojaf/fsxpired/issues).

## Also

- [Version and prerelease rules](versions.html)
- [JSON output](json.html)
- [llms.txt](llms.txt) and [llms-full.txt](llms-full.txt) for agents: the index, and every page in one file.
