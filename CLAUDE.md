# CLAUDE.md

Guidance for AI coding agents working in this repository. Human contributors: see CONTRIBUTING.md.

## Project

Hash Tool — Avalonia 12 / .NET 10 desktop app (`src/HashTool.App`, executable `CheckHash`) plus the `hashtool` CLI
(`src/HashTool.Cli`, NativeAOT), both built on `src/HashTool.Core`. Maintainer: poli0981 (GitHub). The maintainer writes
in Vietnamese; reply in Vietnamese unless asked otherwise. Code, comments, commits and docs are English.

Read docs/ARCHITECTURE.md before larger changes and docs/RELEASING.md before touching packaging or the Release workflow.

## Commands

```text
dotnet build HashTool.slnx -warnaserror                 # CI treats warnings (incl. NuGet audit, analyzers) as errors
dotnet test --solution HashTool.slnx                    # Microsoft.Testing.Platform (global.json); not VSTest
dotnet format HashTool.slnx --verify-no-changes         # CI-enforced
python -I eng/loc.py check                              # translations: keys, placeholders, whitespace, wiring
dotnet src/HashTool.App/bin/Debug/net10.0/CheckHash.dll --smoke-test   # headless end-to-end check
```

Run the app or smoke test with `HASHTOOL_DATA_DIR` pointing at a scratch folder so the user's real settings
(`%LOCALAPPDATA%\HashTool`) are never touched. Chain build/test/format/commit with `&&`, not `;`.

## Rules that are easy to break

- **Velopack/update invariants** (docs/RELEASING.md): pack id `HashTool`, channels `win`/`osx` (+ `osx-x64`, `linux`),
  main exe `CheckHash(.exe)`, macOS bundle id `com.HashTool.HashTool` (build/macos/Info.plist), no `--packTitle` on
  Windows, tags without `v`, settings path. Changing any of these strands 1.x users.
- Never run a built `HashTool-*-Setup.exe`/`.pkg` on the maintainer's machine; test updates with portable builds in a
  scratch folder (the Release workflow's upgrade gate does this in CI).
- Every UI string goes through `Localization[Key]` / `L["Key"]` and must exist in all 67 resx files; add keys with
  `eng/loc.py set` (JSON `{culture: {key: value}}`, `"neutral"` = English). App.Tests fail on missing, unused or
  mismatched keys. Keep "Hash Tool" untranslated inside sentences.
- Core is UI-free and AOT/trim compatible: no reflection-based JSON (use the source-generated contexts), keep
  `ConfigureAwait(false)` (Meziantou MA0004 is on for Core and CLI, off for App/tests).
- Culture: use invariant formatting for logs, file names and machine-readable output; the UI uses the selected culture.
- CRC32 is big-endian (standard); `HashMatcher` accepts the 1.1 little-endian form as `LegacyByteOrderMatch`.
- Checksum entries must go through `ChecksumEntry.ResolvePath` (rejects UNC/device paths).
- GitHub Actions: pin every action to a full commit SHA with a version comment, `persist-credentials: false`, per-job
  minimal `permissions`, no `${{ }}` inside `run:` (pass through `env:`). zizmor and actionlint run in the Security
  workflow; `ci-ok` is the single required check.
- Don't embed C# or regex text containing backslashes in Python/bash heredocs — the escapes get mangled. Edit such
  files with the editor tools.
- .NET tools (vpk, nuget-license, CycloneDX, dotnet-sonarscanner, dotnet-coverage) live in `.config/dotnet-tools.json`;
  Dependabot doesn't read a root `dotnet-tools.json`. vpk and the Velopack package move together.

## Workflows

- `ci.yml` — build/test on Windows, macOS, Linux; coverage gate (Core ≥ 80%); smoke test; format; translation check.
- `security.yml` — vulnerable packages, licences (eng/allowed-licenses.json), gitleaks, actionlint, zizmor.
- `release.yml` — manual (`workflow_dispatch`, version + publish) with an upgrade gate from 1.1.3; dry run on PRs that
  touch release files. Discord notifications use the maintainer's `poli0981/.github` reusable workflows, pinned by SHA.
- `sonarcloud.yml` — SonarCloud CI analysis (Automatic Analysis is off; project `poli0981_HashTool`): scanner build,
  Cobertura coverage from the tests and the smoke test (`dotnet-coverage`), "Sonar way" gate on new code. Skipped when
  `SONAR_TOKEN` isn't available (forks, Dependabot). Issues and hotspots can't be resolved from here; fix them in code.
