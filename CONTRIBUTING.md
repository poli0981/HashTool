# Contributing to HashTool

Thanks for taking an interest in HashTool. Your contributions help make it better for everyone.
Whether you're reporting a bug, suggesting a new feature, fixing a translation or submitting code, every contribution is appreciated.

This project is still small and solo-maintained, so keep it simple and direct — I read everything.

## Project status: maintenance mode

Since version 1.3.0 (November 2026), Hash Tool is in **maintenance mode**: it does what it set out to do, and the work
now goes into keeping it secure, working and accessible rather than into new features.

**Accepted:**

- **Security fixes.** Vulnerabilities rated *high* or *critical* in the .NET runtime or a dependency, and other serious
  security problems, are fixed as soon as practical, with a new release.
- **Regular dependency updates** — .NET servicing releases, Avalonia and the other packages, CI tooling (Dependabot).
- **Major platform updates**, such as moving to a new .NET version when it is worth it. Hash Tool stays on .NET 10 (LTS)
  for now.
- **Bug fixes**, including UI and UX problems.
- **Accessibility fixes** — keyboard, screen readers, contrast, scaling ([ACCESSIBILITY.md](ACCESSIBILITY.md)).
- **Translation fixes** — wording that is wrong, misleading, unnatural or culturally inappropriate, and typos that
  change the meaning.
- **Hash algorithms** — adding or removing one when someone really needs it (see [below](#hash-algorithms)).

**Not accepted:** new features, unless one is truly necessary. Feature requests and pull requests that add features
will usually be closed with a short explanation. If you have an idea, start a discussion in
[Ideas](https://github.com/poli0981/HashTool/discussions/categories/ideas) before writing code.

## Reporting a problem

Open an [issue](https://github.com/poli0981/HashTool/issues/new/choose) and pick **Bug report**, **Accessibility issue**,
**Translation problem** or **Hash algorithm request** — the form asks for what's needed (version, OS, steps, logs).
Questions are welcome in [Discussions](https://github.com/poli0981/HashTool/discussions).

Security problems: please **don't** open a public issue — see [SECURITY.md](SECURITY.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.404 or newer) — `global.json` picks it up. Python 3 is only needed for the translation tool.

```text
dotnet build HashTool.slnx -warnaserror          # what CI runs; warnings are errors
dotnet test --solution HashTool.slnx             # Core, CLI and headless UI tests
dotnet format HashTool.slnx --verify-no-changes  # formatting, enforced by CI
dotnet run --project src/HashTool.App            # start the app
dotnet src/HashTool.App/bin/Debug/net10.0/CheckHash.dll --smoke-test   # headless self-check
```

Set `HASHTOOL_DATA_DIR` to a scratch folder when you run the app during development, so it does not touch your real settings.

How the code is organised: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Submitting code (pull requests)

1. Fork the repo and create a branch: `fix/crash-on-large-files`, `a11y/row-names` or `l10n/vi-vn-status`. For anything
   bigger than a small fix, open an issue first so we can agree on it.
2. Keep the change focused; follow the existing style (the analyzers and `.editorconfig` help).
3. Add or update tests — hashing and checksum logic belongs in `HashTool.Core` with tests in `tests/HashTool.Core.Tests`.
4. Add a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything users will notice.
5. Open the pull request and link the issue. CI builds and tests on Windows, macOS and Linux, and scans dependencies, licences, secrets and workflows; everything must be green before merging. SonarCloud also analyses the pull request: new code should pass its quality gate (A ratings, at least 80% coverage, little duplication).

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (`fix(core): …`, `feat(app): …`, `docs: …`).

## Translations

All UI text lives in `src/HashTool.App/Lang/Resources.*.resx` (English is `Resources.resx`; 66 other languages). Use the helper instead of editing XML by hand:

```text
python -I eng/loc.py check                 # same keys and placeholders everywhere, no stray whitespace
python -I eng/loc.py untranslated vi-vn    # strings that are still English
python -I eng/loc.py export vi-vn          # a language as JSON
python -I eng/loc.py set changes.json      # {"vi-vn": {"Key": "text"}, "neutral": {...}}
```

New text needs a key in **every** file — the localization tests fail otherwise. Keep placeholders such as `{0}` exactly, and keep the product name "Hash Tool". Corrections from native speakers are very welcome; most translations were made with AI assistance.

## Hash algorithms

Hash Tool supports MD5, SHA-1, SHA-256, SHA-384, SHA-512, BLAKE3, xxHash32, xxHash64, XXH3, XXH128 and CRC32. A new one
is added when someone needs it to check real files — open a
[Hash algorithm request](https://github.com/poli0981/HashTool/issues/new?template=hash_algorithm.yml) and say where it is
used. Candidates already looked at, in order of how likely they are to be useful:

| Algorithm | Where it is used | Notes |
|---|---|---|
| BLAKE2b-512 | `b2sum` (GNU coreutils), the `b2sums.txt` files of Arch Linux | Not in .NET; needs a small implementation or package. Same length as SHA-512. |
| SHA3-256, SHA3-512 | FIPS 202, `openssl dgst -sha3-256` | The SHA-3 of .NET is missing on macOS and Windows 10, so it needs a managed implementation. Same lengths as SHA-256 and SHA-512. |
| CRC-32C | iSCSI, ext4, Btrfs, cloud storage | .NET only has the instruction (`BitOperations.Crc32C`). Same length as CRC32; cloud services usually show it in Base64. |

How an algorithm is wired in, and what to watch for: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#adding-a-hash-algorithm).

## Accessibility

UI changes must keep the app usable with the keyboard alone and with screen readers: translated names for controls,
Tab reaching every control, no colour-only signals, and readable contrast. [ACCESSIBILITY.md](ACCESSIBILITY.md) has the
checklist, the automated tests that enforce it and how to check by hand.

## Code of Conduct

Please be respectful — see [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details.

Thanks again for helping make HashTool better.

© 2026 poli0981. All rights reserved.
