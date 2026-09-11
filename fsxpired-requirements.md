# fsxpired: outdated NuGet references in F# scripts

A dotnet tool that reads `.fsx` files, finds every `#r "nuget: ..."` reference, and reports which
ones have a newer version on nuget.org. With `-u` it rewrites the pins in place.

Nothing like it exists as of 2026-09-11: `dotnet outdated` reads project files only, Dependabot
does not know the `#r "nuget:"` form, and Renovate covers it only through a custom regex manager.

## Naming

`fsxpired`. Package id, tool command, and repository all use it. The tool command is
`dotnet fsxpired`, or `fsxpired` when installed globally.

## Scope

In:

- `#r "nuget: Id"` and `#r "nuget: Id, Version"` directives, with the whitespace FSI allows
  around the colon and the comma, including no space at all (`nuget:Newtonsoft.Json`).
- `#load` directives: followed when they point at a `.fsx` file, so a script that loads helpers
  reports the helpers' references too, once each, attributed to the file that holds them. A
  `#load` of a `.fs` file is skipped: a `.fs` file cannot hold `#r`. A `#load` target that does
  not exist is an error.
- Exact versions, ranges and wildcards as FSI accepts them (`1.0`, `[1.0]`, `>= 1.0`, `1.*`).
  "Outdated" means the newest version the reference resolves to is behind the newest available,
  not that two strings differ.
- Prerelease awareness. A stable pin is compared against stable versions unless `--prerelease`
  is given. A prerelease pin is always compared against everything: something already on a beta
  needs no flag to move to the next beta, or to the stable release once it exists.
- One HTTP lookup per distinct package id per run, cached for the run. Scripts in the same
  repository share packages, and the same id must not be queried twice.
- A table per file, then a summary line. Exit codes below.
- `-u` / `--update`: rewrite exact pins in place, in loaded files too.
- `doctor <file>`: walk one script through every step and report what happened at each.

Out, for the first version, and each one stops the run with exit 2 and a message that names the
file and line and asks for an issue:

- `#i "nuget: <url>"`. Only nuget.org is queried. A script that adds a feed would get wrong
  answers, and a wrong "up to date" is worse than none.
- A version text that `NuGet.Versioning` cannot parse, such as the `{{fsdocs-package-version}}`
  placeholder fsdocs substitutes in literate scripts.

Out, silently:

- Paket, `#r "paket:"`, or `paket.dependencies`. Paket has `paket outdated`.
- `#r` of a DLL path or a project. Nothing on a feed to compare against.
- Transitive dependencies. Only what the script names.
- Directives inside `#if` blocks other than `INTERACTIVE`. The script is parsed the way FSI
  sees it, with `INTERACTIVE` defined and nothing else.
- Feeds other than nuget.org, credentials, `NuGet.config` sources, and offline mode.

## Command line

```
fsxpired [...flags] [...paths]
fsxpired doctor <file>
```

Positional arguments are files or folders. A folder is searched recursively for `*.fsx`, skipping
`bin`, `obj`, `node_modules`, `.git` and anything a `.gitignore` excludes. No arguments means the
current directory. A file given explicitly and also reached through `#load` is reported once.

| Flag | Meaning |
| --- | --- |
| `-u`, `--update` | Rewrite each outdated exact pin to the latest version it may move to |
| `-pre`, `--prerelease` | Let stable pins move to, and be compared against, prereleases |
| `--json` | Write the report as JSON to stdout, everything else to stderr |
| `--version` | Print the version and exit |
| `-h`, `--help` | Print the help page and exit |

Argument parsing is hand-rolled, as in Fantomas: a typed list of problems, typo suggestions by
edit distance, and a data-driven flag list that also renders the help page, so the page cannot
drift from what the parser accepts. The help page follows the Fantomas layout: one line of
purpose with the version, `Usage:`, `Examples:`, `Commands:`, `Flags:`, `Paths:`, links. The
invocation name in examples matches how the tool was started, `dotnet fsxpired` or `fsxpired`.

There is no verbosity flag. `doctor` is the verbose mode.

## Output

Streams: the report goes to stdout and nothing else does. Messages about the run, such as a file
that could not be read, go to stderr. `fsxpired --json | jq` never breaks.

Table, the default, one box-drawn table per file with the file path as heading, then a summary
line. A Note column appears only when a row has a note.

```
build.fsx
╭────────────────┬─────────┬────────┬────────────┬────────────────────────────────────────────────────╮
│ Package        │ Current │ Latest │ Status     │ Note                                               │
├────────────────┼─────────┼────────┼────────────┼────────────────────────────────────────────────────┤
│ Fun.Build      │ 1.1.18  │ 1.1.20 │ outdated   │                                                    │
│ FSharp.Data    │ 6.3.0   │ 6.3.0  │ up to date │                                                    │
│ Humanizer.Core │         │ 3.0.10 │ floating   │ no version pinned, resolves to latest on every run │
╰────────────────┴─────────┴────────┴────────────┴────────────────────────────────────────────────────╯

scripts/BuildAnalyzers.fsx
╭─────────────┬─────────┬────────┬────────────╮
│ Package     │ Current │ Latest │ Status     │
├─────────────┼─────────┼────────┼────────────┤
│ FSharp.Data │ 6.3.0   │ 6.3.0  │ up to date │
╰─────────────┴─────────┴────────┴────────────╯

1 outdated, 1 floating in 2 files
```

Files without references are not listed. When no reference was found at all, the summary says so.

- `floating` is a reference without a version. It resolves to latest at every run, so it is shown
  in yellow with the note "no version pinned, resolves to latest on every run", and listed in the
  summary, but it never affects the exit code. `-u` leaves it alone.
- A range or wildcard is shown as written, with what it resolves to in parentheses, and is
  rewritten by nothing; a range is a deliberate choice, so its status is `up to date` and the
  Latest column shows the newest version that exists.
- With `-u`, the status of a rewritten reference reads `updated 1.1.18 -> 1.1.20`.
- A package nuget.org does not know is `unknown` with a note, and does not affect the exit code.

The table is rendered with Spectre.Console. Box drawing and colours only when stdout is a
terminal; ASCII and no colour when redirected, when `NO_COLOR` is set, or in tests. stdout and
stderr are detected separately, as Fantomas does.

JSON, with `--json`, follows the Fantomas envelope and is written with `Utf8JsonWriter`, no
serializer:

```json
{
  "command": "outdated",
  "workingDirectory": "/home/nojaf/Projects/fantomas",
  "exitCode": 1,
  "error": null,
  "summary": { "outdated": 1, "floating": 1, "unknown": 0, "updated": 0, "files": 2 },
  "files": [
    {
      "path": "build.fsx",
      "references": [
        { "id": "Fun.Build", "line": 3, "version": "1.1.18", "resolved": "1.1.18",
          "latest": "1.1.20", "status": "outdated", "note": null }
      ]
    }
  ]
}
```

`status` is one of `outdated`, `current`, `floating`, `unknown`, `updated`. The shape is not a
contract; the exit code is the promise. LLM agents are the intended reader.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Nothing outdated, or every outdated pin was rewritten by `-u` |
| 1 | At least one reference is outdated |
| 2 | Unexpected error: a file or `#load` target could not be read, nuget.org could not be reached, an unsupported `#i` or version text was met, or an argument was wrong |
| 3 | `-u` could not write a file |

An error wins over an outdated result.

## Update

`-u` rewrites exact pins only. The parser records the exact character span of each version text,
and the rewrite replaces that span and nothing else: whitespace, quoting and the rest of the line
stay as written, as does the file's line ending and encoding. Files reached through `#load` are
rewritten too, since that is where the reference lives. The run without `-u` is the dry run;
there is no `--dry-run` flag.

## Doctor

`fsxpired doctor <file>` takes one `.fsx` file and refuses a folder. It walks the file through
gated steps, each reported, each stopping the walk when it fails:

1. File: the absolute path, whether it exists and is a script.
2. Parse: every hash directive found with its line number, including the ones ignored and why.
3. Loads: every `#load` resolved to an absolute path, or reported missing.
4. Feed: whether the nuget.org service index responded.
5. References: for each id, how many versions were fetched, the latest stable and the latest
   prerelease, the version the reference resolves to, and the verdict with its reason.

The header prints the tool version, the dotnet version, and the working directory. Exit 1 when a
step failed, 0 otherwise. Unlike the default command, `doctor` does not stop on an `#i` line or a
version text it cannot parse: it reports them as the step's finding and continues, since
explaining is its job.

## Resolution rules

- Versions come from NuGet.Protocol's `FindPackageByIdResource` against
  `https://api.nuget.org/v3/index.json`. Package ids are compared case-insensitively.
- Versions and ranges are parsed with `NuGet.Versioning`. No hand-written version parser.
- The feed sits behind an interface so tests hand the resolver a fake with a fixed version list,
  and so a second feed is a small change later.

## Shape of the code

One CLI project, `src/Fsxpired`, and one test project, `src/Fsxpired.Tests`. No library project:
the seams are modules, not packages.

1. **Parse**: script text in, list of references out. Uses Fantomas.FCS to parse the script with
   `INTERACTIVE` defined and reads `ParsedHashDirective` nodes from the AST, so comments, strings
   and spacing are handled exactly as FSI handles them. Each reference records file, line, id,
   the version text as written, and its character span. Follows `#load` through a file system
   abstraction.
2. **Resolve**: reference list in, one lookup per distinct id, results out. Feed behind an
   interface.
3. **Report**: results in, table or JSON out.
4. **Update**: results and file contents in, rewritten contents out. Pure.

The command line binds them through a `run argv env` entry point, where `env` carries the
working directory, stdout and stderr writers, the Spectre console, the file system
(System.IO.Abstractions), and the feed. Tests call `run` in process.

## Testing

NUnit, FsUnit, and Verify.NUnit for snapshots. No process-level test unless a behaviour cannot be
proven any other way.

- Parser: one sample script or folder per case, snapshot of the parsed reference list. Cases are
  the forms found under `~/Projects` on 2026-09-11: exact pin, no space after the colon,
  extra whitespace, no version, `6.*`, prerelease pin, a `#load` chain with a helper shared by two
  roots, a `#load` of a missing file, a `#load` of a `.fs` file, `#r` inside a block comment,
  `#r` inside a trailing line comment after a DLL reference (as in the fantomas docs), the fsdocs
  placeholder version, and an `#i` line. Do not go beyond what real scripts contain.
- Resolver: range semantics against a fake feed, as plain asserts. `1.*` with `1.5.0` and
  `2.0.0` available is current; `>= 1.0` with `2.0.0` available is current; `1.0.0` with `1.0.1`
  available is outdated; `1.0.0` is current when only `1.1.0-beta` is newer, unless
  `--prerelease`; `1.0.0-beta1` with `1.0.0-beta2` available is outdated without any flag.
- CLI: snapshots of stdout and stderr for the table, `--json`, `--help`, each exit code, and
  `doctor`, through `run` with a plain console.
- Update: copy a sample folder to a temporary directory, run with `-u` against the fake feed,
  snapshot the rewritten files.
- End to end, by hand: run against `~/Projects/fantomas` and compare with the `#r` lines.

## Repository

Conventions copied from Fantomas:

- `Directory.Build.props`: `WarningsAsErrors` FS0025 and FS1182, lock files in locked mode,
  `UseArtifactsOutput`, reproducible builds, `ChangelogFile` pointing at `CHANGELOG.md`.
- `.gitattributes` forcing LF: the parser tests record character offsets into the sample
  scripts, and the snapshots are compared byte for byte.
- `Directory.Packages.props` with central package management and transitive pinning.
- `NuGet.config` with a cleared source list, nuget.org only, and source mapping.
- `global.json` pinned to the 10.0.100 band, `rollForward: latestMinor`.
- `fsxpired.slnx`, `.editorconfig` with the Fantomas formatting settings, `.fantomasignore`.
- `dotnet-tools.json` at the repository root, where the .NET 10 SDK template puts it: fantomas,
  fsharp-analyzers, fsdocs-tool 23.0.0-alpha.2.
- Ionide.Analyzers and G-Research.FSharp.Analyzers referenced with `PrivateAssets=all`.
- `build.fsx` with Fun.Build: `Build` pipeline (restore tools, check format, build, test, pack,
  docs), `Analyze`, `FormatAll`, `Docs` watch, `Release`. No dogfooding stage: the repo's own
  `build.fsx` is not forced onto the latest of everything.

Docs with fsdocs, published to GitHub Pages from the main workflow: a landing page with install
and usage, a page on the JSON output, a page on version and prerelease rules, and `llms.txt`.
No API docs; this is a tool, not a library.

## Release

- `CHANGELOG.md` in Keep a Changelog format drives the version through Ionide.KeepAChangelog.
- CI on pull requests and pushes to main builds and tests on ubuntu, windows and macos.
- Push to main runs the `Release` pipeline: read the top changelog entry, ask `gh release view`
  whether that version already has a GitHub release, and stop if it does. Otherwise pack, push to
  nuget.org with NuGet trusted publishing (`NuGet/login` with OIDC, `environment: release`,
  `id-token: write`), and create the GitHub release with the changelog section as notes. No
  drafts.
- Start at 0.1.0, MIT, author Florian Verdonck.

## Later

- `--source <url>` and `#i`, once someone has the use case.
- `--offline` against the global packages folder.
- `--pin` to turn floating references into exact pins, and `--fail-on-floating`.
- A GitHub Action wrapper, which is mostly `dotnet tool install` plus the JSON output.
- Ionide integration: the parser is the reusable part, and a code lens on each `#r` line is the
  natural home for the result.
