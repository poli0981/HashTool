# Releasing

Releases are built and published by the **Release** workflow (`.github/workflows/release.yml`). Nothing is built on a
developer machine.

## Steps

1. Make sure `main` is green (CI, Security).
2. On `main`, in one commit `chore(release): X.Y.Z`:
   - set `<Version>` in `Directory.Build.props`;
   - rename `## [Unreleased]` / `## [X.Y.Z] - Unreleased` in `CHANGELOG.md` to `## [X.Y.Z] - YYYY-MM-DD`.
3. *Actions → Release → Run workflow* on `main` with `version = X.Y.Z` (no `v`) and **publish unchecked** — a dry run.
4. When it is green, run it again with **publish checked**.

The workflow refuses to publish when the version does not match `Directory.Build.props`, the CHANGELOG heading has no
date, the run is not on `main`, or the release already exists. Pull requests that change the release machinery get a
dry run automatically (version `X.Y.Z-ci.<run>`).

## What the workflow does

| Job | Runner | Output |
|---|---|---|
| `prepare` | ubuntu | version, release notes from CHANGELOG |
| `app (win)` | windows-2025 | `HashTool-win-Setup.exe`, `HashTool-win-Portable.zip`, `HashTool-X.Y.Z-full.nupkg`, `releases.win.json`, `RELEASES` |
| `app (osx)` | macos-15 (arm64) | `HashTool-osx-Setup.pkg`, `HashTool-osx-Portable.zip`, `HashTool-X.Y.Z-osx-full.nupkg`, `releases.osx.json` |
| `app (osx-x64)` | macos-15-intel | the same for Intel Macs (channel `osx-x64`) |
| `app (linux)` | ubuntu-24.04 | `HashTool.AppImage`, `HashTool-X.Y.Z-linux-full.nupkg`, `releases.linux.json` |
| `upgrade-gate` | windows-2025, macos-15 | proves a 1.1.3 install updates to the new version |
| `cli` | all four | `hashtool-X.Y.Z-<rid>.zip/.tar.gz` (NativeAOT; Linux built on ubuntu-22.04 for older glibc) |
| `publish` | ubuntu | SBOMs, `SHA256SUMS`, provenance attestation, draft release → verify uploads → publish |
| `announce` | — | Discord announcement through the shared `poli0981/.github` workflow |

Each `app` job publishes self-contained single-file builds, starts them headless (`--smoke-test --expect-version`),
packs them with `vpk` and checks the result with `eng/assert-release.py`.

## Invariants — never change these

Hash Tool 1.0–1.1 users update through Velopack and only find a release when these stay the same:

| | Windows | macOS (Apple silicon) |
|---|---|---|
| Velopack pack id | `HashTool` | `HashTool` |
| Channel | `win` (x64) | `osx` (arm64) |
| Main executable | `CheckHash.exe` (`<AssemblyName>CheckHash</AssemblyName>`) | `CheckHash` |
| Title / bundle | no `--packTitle` (title "HashTool"), shortcuts Desktop + Start menu | `Hash Tool.app`, bundle id `com.HashTool.HashTool` (kept by `build/macos/Info.plist`) |

Also: tags have no `v` prefix (`1.2.0`), releases are not pre-releases, and settings stay in
`LocalApplicationData/HashTool/settings/config.json`. New platforms get new channels (`osx-x64`, `linux`).

Releases are published with `--delta None`: every update downloads the full package, which old clients handle best.

## The upgrade gate

`tests/Compat/LegacyClient` references **Velopack 0.0.1298**, the library inside Hash Tool 1.1.x. For Windows and macOS
the gate downloads the real 1.1.3 portable build, uses that library to read the new feed and download the update (with
its checksum checks), applies it with the **1.1.3 updater** (`Update.exe` / `UpdateMac` `apply --package --norestart`)
and starts the updated app with `--smoke-test --expect-version`. Publishing is blocked if any step fails.

## After publishing

- Check the release page lists every asset and that `SHA256SUMS` matches (`sha256sum -c SHA256SUMS --ignore-missing`).
- Update an existing installation through *Update → Check Now* on at least one machine.
- If something is wrong, publish a fixed `X.Y.Z+1` instead of editing or deleting the release: clients may already have
  downloaded it.
