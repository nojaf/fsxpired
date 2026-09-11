---
title: JSON output
category: Documentation
categoryindex: 1
index: 3
---

# JSON output

`--json` writes one document to stdout and everything else to stderr, so `fsxpired --json | jq`
never breaks. The shape is not a contract; the exit code is the promise. It mirrors the table with
the fields a script needs to act on a reference.

```json
{
  "command": "outdated",
  "workingDirectory": "/home/me/repo",
  "exitCode": 1,
  "error": null,
  "summary": { "outdated": 1, "floating": 1, "unknown": 0, "updated": 0, "files": 1 },
  "files": [
    {
      "path": "build.fsx",
      "references": [
        {
          "id": "Fun.Build",
          "line": 3,
          "version": "1.1.18",
          "resolved": "1.1.18",
          "latest": "1.1.20",
          "status": "outdated",
          "note": null
        },
        {
          "id": "Humanizer.Core",
          "line": 4,
          "version": null,
          "resolved": "3.0.10",
          "latest": "3.0.10",
          "status": "floating",
          "note": "no version pinned, resolves to latest on every run"
        }
      ]
    }
  ]
}
```

- `command` is `outdated` or `update`.
- `error` is always present, `null` when the run got to a report. When it is set, `files` is
  empty and the same message went to stderr.
- `path` is relative to `workingDirectory`, with forward slashes on every platform.
- `version` is the text as written, `null` for a floating reference.
- `resolved` is what FSI resolves the reference to today; `latest` is the newest version it may
  move to under the run's prerelease rule. Either is `null` when nuget.org does not know the id.
- `status` is one of `outdated`, `current`, `floating`, `unknown`, `updated`.
