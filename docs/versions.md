---
title: Version and prerelease rules
category: Documentation
categoryindex: 1
index: 2
---

# Version and prerelease rules

## How a version text is read

| Written as | Kind | Resolves to | `-u` |
| --- | --- | --- | --- |
| `1.2.3` | exact pin | that version | rewrites it |
| `6.*`, `*` | wildcard | the highest match | leaves it |
| `[1.0.0]`, `[1.0,2.0)` | range | the lowest match, as NuGet does | leaves it |
| nothing | floating | the latest at every run | leaves it |

Versions and ranges are parsed with NuGet.Versioning, so what NuGet accepts, fsxpired accepts.

## Outdated

An exact pin is outdated when a newer version exists that it may move to. Which versions count
depends on the prerelease rule:

- A **stable pin** is compared against stable versions only. `1.0.0` is up to date when the only
  newer version is `1.1.0-beta`.
- With **`-pre` / `--prerelease`**, a stable pin is compared against everything, and `-u` moves
  it to a prerelease when that is the newest.
- A **prerelease pin** is always compared against everything. `8.0.0-beta-001` is outdated once
  `8.0.0-beta-002` exists, and again once `8.0.0` exists, with no flag needed: the pin already
  opted in.

A range is never outdated. It is shown with what it resolves to today and the latest version that
exists, so the gap is visible, and left alone.

## Update

`-u` replaces the version text of each outdated exact pin with the version it may move to. Only
that span changes: spacing, quoting, line endings and the byte order mark stay as written. Files
reached through `#load` are rewritten too. The run without `-u` is the dry run.
