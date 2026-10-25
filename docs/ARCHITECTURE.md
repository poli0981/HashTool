# Architecture

HashTool is a .NET 10 solution with three shipped programs sharing one core library.

```
src/HashTool.Core   hashing, checksum files, folder walking, settings (no UI; trim/AOT compatible)
src/HashTool.App    Avalonia 12 desktop app (CheckHash.exe) — views, view models, services
src/HashTool.Cli    `hashtool` command-line tool (System.CommandLine, NativeAOT)
tests/              Core.Tests, Cli.Tests, App.Tests (Avalonia headless); Compat/LegacyClient (release gate)
bench/              BenchmarkDotNet benchmarks (run by hand)
eng/                scripts used by CI and releases (translations, coverage, release notes, package checks)
build/              packaging inputs (macOS Info.plist, Linux icon)
```

Shared build settings: `Directory.Build.props` (version, nullable, analyzers, NuGet audit), `Directory.Packages.props`
(central package versions with transitive pinning), `global.json` (SDK and the Microsoft.Testing.Platform runner),
`.config/dotnet-tools.json` (vpk, nuget-license, CycloneDX, SonarScanner for .NET, dotnet-coverage; Dependabot keeps
them up to date).

## HashTool.Core

| Area | Types | Notes |
|---|---|---|
| Hashing | `HashEngine`, `HashType`, `HashAlgorithms`, `HashResult`, `HashMatcher` | One read per file for any number of algorithms: double-buffered reads (1 MiB, 4 MiB above 1 GiB), each block fed to every algorithm in parallel; BLAKE3 uses its multi-threaded update for large blocks. CRC32 is big-endian (as 7-Zip/SFV); `HashMatcher` still accepts the reversed order Hash Tool 1.1 wrote. |
| Checksum files | `ChecksumParser`, `ChecksumWriter`, `ChecksumDocument`, `ChecksumFileNames` | Reads GNU (`hash  name`, `*name`, escaped names), BSD tagged, SFV, xxhsum `XXH3_` and bare sidecar hashes; writes GNU/BSD/SFV/JSON/CSV. The algorithm comes from the line, then the file name, then the user's choice, then the hash length. Paths may not point to UNC or device locations; CSV output neutralises formulas. |
| Files | `FileEnumerator`, `FileErrors` | Walks folders without following symbolic links or junctions, skips unreadable folders, optionally hidden files. |
| Processing | `BatchProcessor`, `HashJob`, `IHashJobObserver<T>` | Small files in parallel (default `min(cores, 4)`), files ≥ 1 GiB in a separate lane, one at a time in HDD mode; per-file time-out; every item is reported exactly once. |
| Configuration | `AppSettings`, `SettingsStore` | `config.json` with a schema version; atomic writes (temp file + move), values clamped on load, a corrupt file is backed up as `config.json.bak-<time>`, 1.x font settings are migrated. |

## HashTool.App

- **Composition** — `AppHost` builds a `Microsoft.Extensions.DependencyInjection` container (services and view models are
  singletons). `Program.Main` runs Velopack hooks first, then `--smoke-test`, single-instance forwarding and Avalonia.
- **Views and view models** — compiled bindings everywhere; `App.axaml` maps each view model to its view.
  `FileListViewModelBase` holds what Generate and Verify share: importing (background enumeration, batches of 1000),
  filtering, running a `BatchProcessor` with UI updates pumped every 100 ms, and the summary dialog.
- **Localization** — `LocalizationService` reads the satellite resources, detects the system language, sets the culture
  (including `DefaultThreadCurrentCulture`, so async work keeps it) and right-to-left flow. Views bind
  `Localization[Key]`; view models swap their `LocalizationProxy` when the language changes, which refreshes every
  binding. Status texts are stored as enums and translated by converters, so they follow language changes.
- **Services** — dialogs, file pickers, clipboard and launcher behind interfaces (`UiServices.cs`); `SettingsService`
  debounces saves; `ThemeService`/`ThemePalettes` swap brush resources; `FontService` picks per-script fonts;
  `UpdateService` wraps Velopack (GitHub source, stable channel).
- **Infrastructure** — logging to daily files and an in-memory sink for the Developer console (`Logging/`), crash
  reports, `AppPaths` (`HASHTOOL_DATA_DIR` override), command-line parsing, single instance (named mutex/lock file plus
  a per-user named pipe), Windows shell integration under `HKCU\Software\Classes`, on-demand elevation, and the
  headless smoke test.

## HashTool.Cli

`hashtool hash | verify | check | algorithms`, built on Core. Exit codes follow `sha256sum`: 0 success, 1 failed or
mismatched file, 2 usage error or unreadable checksum file. Published per platform as a single NativeAOT executable.

## Tests

- `HashTool.Core.Tests` — known-answer vectors for every algorithm, engine, parser/writer, enumerator, batch processor,
  settings store. CI requires at least 80% line coverage of Core.
- `HashTool.Cli.Tests` — commands, output formats and exit codes.
- `HashTool.App.Tests` — Avalonia headless: every page in several languages, a create-and-verify flow, translation
  integrity (keys, placeholders, Languages.json ↔ resx ↔ project, satellite assemblies, unused keys), language detection.
- `tests/Compat/LegacyClient` — not in the solution: the release workflow uses it to update a real 1.1.3 install with
  its own Velopack 0.0.1298 library and updater (see [RELEASING.md](RELEASING.md)).

[SonarCloud](https://sonarcloud.io/project/overview?id=poli0981_HashTool) analyses every pull request and `main` from
the SonarCloud workflow: the C# during a real build, with coverage from the tests above and from the smoke test
(`dotnet-coverage`), plus the scripts and workflows. Its quality gate ("Sonar way") applies to new code; `bench/` is
analysed as test code and `eng/` is left out of coverage.

## Benchmarks

```text
dotnet run -c Release --project bench/HashTool.Benchmarks -- --filter '*'
```

`LargeFileBenchmarks` compares the engine with `SHA256.HashData(FileStream)`; `SmallFilesBenchmarks` compares sequential
hashing with `BatchProcessor`. On an i7-14700KF with a warm cache: SHA-256 of 256 MiB 129 ms vs 221 ms, BLAKE3 57 ms,
XXH3 36 ms, CRC32 34 ms; 2,000 × 16 KiB files 26 ms in parallel vs 89 ms sequentially.
