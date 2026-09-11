<p align="center">
  <img src="https://raw.githubusercontent.com/nojaf/fsxpired/main/assets/logo.png" alt="fsxpired logo" width="160">
</p>

# fsxpired

Reports which `#r "nuget: ..."` references in your F# scripts have a newer version on nuget.org,
and updates them when asked.

```
dotnet tool install -g fsxpired
fsxpired            # every .fsx under the current folder
fsxpired build.fsx  # one script, and everything it #loads
fsxpired -u         # rewrite outdated pins in place
fsxpired --json     # for scripts and agents
```

Exit codes: 0 nothing outdated, 1 something is outdated, 2 unexpected error, 3 a file could not
be written.

See the [documentation](https://nojaf.github.io/fsxpired/) for the details.
