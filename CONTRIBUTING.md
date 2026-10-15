# Contributing to HashTool

Thanks for taking an interest in HashTool. Your contributions help make it better for everyone.
Whether you're reporting a bug, suggesting a new feature, fixing a translation or submitting code, every contribution is appreciated.

This project is still small and solo-maintained, so keep it simple and direct — I read everything.

## Reporting bugs or requesting features

Open an [issue](https://github.com/poli0981/HashTool/issues/new/choose) and pick **Bug report** or **Feature request** — the form asks for what's needed (version, OS, steps, logs).
Questions and ideas are welcome in [Discussions](https://github.com/poli0981/HashTool/discussions).

Security problems: please **don't** open a public issue — see [SECURITY.md](SECURITY.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.400 or newer) — `global.json` picks it up. Python 3 is only needed for the translation tool.

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

1. Fork the repo and create a branch: `fix/crash-on-large-files` or `feature/new-algorithm`.
2. Keep the change focused; follow the existing style (the analyzers and `.editorconfig` help).
3. Add or update tests — hashing and checksum logic belongs in `HashTool.Core` with tests in `tests/HashTool.Core.Tests`.
4. Add a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything users will notice.
5. Open the pull request and link the issue. CI builds and tests on Windows, macOS and Linux, and scans dependencies, licences, secrets and workflows; everything must be green before merging.

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

## Code of Conduct

Please be respectful — see [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details.

Thanks again for helping make HashTool better.

© 2026 poli0981. All rights reserved.
