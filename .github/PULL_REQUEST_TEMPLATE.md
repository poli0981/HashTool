## What and why

<!-- What does this change, and why is it needed? Link the issue it fixes (Fixes #123). -->

## Type of change

<!-- Hash Tool is in maintenance mode: these are the changes it accepts (see CONTRIBUTING.md#project-status-maintenance-mode).
     New features are generally not accepted; please discuss an idea in Discussions before working on it. -->

- [ ] Bug fix, including UI/UX problems
- [ ] Accessibility fix
- [ ] Translation fix
- [ ] Hash algorithm added or removed (link the request)
- [ ] Security fix or dependency/.NET update
- [ ] Documentation, tests or CI

## How it was tested

<!-- Commands you ran, platforms you tried, screenshots for UI changes. -->

## Checklist

- [ ] `dotnet build HashTool.slnx -warnaserror` and `dotnet test --solution HashTool.slnx` pass
- [ ] `dotnet format HashTool.slnx --verify-no-changes` passes
- [ ] New or changed UI text is added to every language (`python -I eng/loc.py check` passes)
- [ ] UI changes work with the keyboard alone, have translated names for screen readers and don't rely on colour alone ([ACCESSIBILITY.md](../ACCESSIBILITY.md#for-contributors))
- [ ] User-visible changes are listed under `[Unreleased]` in CHANGELOG.md
