<p align="center">
  <img src="https://raw.githubusercontent.com/nojaf/fsxpired/main/assets/logo.png" alt="fsxpired logo" width="160">
</p>

# fsxpired

[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/fsxpired?style=flat-square)](https://www.nuget.org/packages/fsxpired/absoluteLatest)
[![llms.txt](https://img.shields.io/badge/llms.txt-1f7a45?style=flat-square)](https://nojaf.com/fsxpired/llms.txt)
[![llms-full.txt](https://img.shields.io/badge/llms--full.txt-1f7a45?style=flat-square)](https://nojaf.com/fsxpired/llms-full.txt)

Reports which `#r "nuget: ..."` references in your F# scripts have a newer version on nuget.org,
and updates them when asked.

```
dnx fsxpired                     # run it once, nothing installed
dotnet tool install -g fsxpired  # keep it around
fsxpired                         # every .fsx under the current folder
fsxpired build.fsx               # one script, and everything it #loads
fsxpired -u                      # rewrite outdated pins in place
fsxpired --json                  # for scripts and agents
```

Exit codes: 0 nothing outdated, 1 something is outdated, 2 unexpected error, 3 a file could not
be written.

See the [documentation](https://nojaf.com/fsxpired/) for the details.
