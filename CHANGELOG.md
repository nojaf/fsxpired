# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-11

### Added

- Report outdated `#r "nuget: ..."` references in `.fsx` files, following `#load` chains.
- `-u` / `--update` rewrites exact pins in place.
- `-pre` / `--prerelease` lets stable pins move to prereleases.
- `--json` for machine-readable output.
- `doctor <file>` walks one script through every step and reports what happened.
