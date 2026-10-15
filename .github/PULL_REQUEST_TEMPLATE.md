## What and why

<!-- What does this change, and why is it needed? Link the issue it fixes (Fixes #123). -->

## How it was tested

<!-- Commands you ran, platforms you tried, screenshots for UI changes. -->

## Checklist

- [ ] `dotnet build HashTool.slnx -warnaserror` and `dotnet test --solution HashTool.slnx` pass
- [ ] `dotnet format HashTool.slnx --verify-no-changes` passes
- [ ] New or changed UI text is added to every language (`python -I eng/loc.py check` passes)
- [ ] User-visible changes are listed under `[Unreleased]` in CHANGELOG.md
