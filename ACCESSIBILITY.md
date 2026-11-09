# Accessibility

**Last reviewed: November 2026 (Hash Tool 1.2.3)**

Hash Tool should work for everyone who needs to check a file: with a keyboard alone, with a screen reader, with large
text or high contrast, and in their own language. This page describes what works today, what does not work yet, and how
to report a problem.

## Goals

The target is [WCAG 2.2](https://www.w3.org/TR/WCAG22/) level AA, where it applies to a desktop app (see
[WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) for how the guidelines map to non-web software) and where Avalonia, the UI
framework, makes it possible. This is a goal, not a claim of conformance: no formal audit has been done.

Priorities, in this order:

1. Every feature works with the keyboard.
2. Screen readers announce a sensible name and role for every control.
3. Text is readable: contrast, text size and themes.
4. The app speaks the user's language, including right-to-left languages.
5. The `hashtool` command-line tool works in any terminal and with screen readers.

## What works today

### Desktop app

**Keyboard.** Tab and Shift+Tab move between controls, the arrow keys move between rows of a file list, and Enter or
Space activates the focused control. In a file row, Tab moves through that row's controls (algorithm, hash, copy, save,
run, clear, remove) and then leaves the list. When a run starts, focus moves from **COMPUTE ALL** / **VERIFY ALL** to
**Cancel**, and back when it ends.

| Keys | Action |
|---|---|
| Ctrl+O (⌘+O on macOS) | Add files |
| Ctrl+Shift+O (⌘+Shift+O) | Add a folder |
| Ctrl+F (⌘+F) | Go to the search box |
| Ctrl+Enter (⌘+Enter) | Compute or verify all |
| Esc | Cancel the run; close a dialog |
| Delete | Remove the selected row |
| Enter, in a dialog | The highlighted button (OK, Yes) |

**Screen readers.** Every button, box, list and row has a translated name: icon buttons are named after their action
("Copy Hash", "Remove File") and rows after their file name. Page titles and section titles are headings, and the
navigation pane and the page are landmarks, so you can jump between them. The update and notice banners and the status
on the Update page are announced when they change. After a run with several files, a summary dialog opens and takes the
focus.

**Results in words, not only colour.** Every status is text: **VALID**, **MISMATCH!**, *Access denied*, *Timed out* and
so on. Search matches are shown in bold as well as in colour.

**Seeing.** You can choose light, dark or the system setting, and eight styles including **High Contrast** and
**Colour-blind** (Okabe–Ito colours). In every style, status text has a contrast of at least 4.5:1 and status icons at
least 3:1; automated tests check this. Settings has the font, a font size from 10 to 32, and a UI scale from 0.8× to
2.5× that also applies to dialogs.

**No mouse or drag and drop needed.** Everything you can drop has a button too: **Add Files…**, **Add Folder…**,
**Open Checksum File…**, and **Browse** for the expected hash of a row. You can also paste the expected hash.

**Language.** The app comes in 67 languages, with right-to-left layouts for Arabic, Persian, Hebrew, Dhivehi and Pashto.
It picks your system language automatically.

**Motion and sound.** The only animation is a 0.3-second colour fade when the theme changes. The app makes no sounds,
and nothing flashes.

### Command-line tool

- Plain text: one line per file, with no colours, spinners or progress bars that redraw the line.
- Exit codes: `0` everything OK, `1` a file failed or did not match, `2` usage error.
- Output for other programs: `hashtool hash -f json` or `-f csv`.
- `hashtool verify -q` prints only the problems.
- Errors go to standard error, in the usual `hashtool: <file>: <reason>` form.
- `hashtool <command> --help` explains each command. [docs/USAGE.md](docs/USAGE.md) lists every option.

## Supported environments

| Platform | Assistive technology | Status |
|---|---|---|
| Windows 10 and 11 | Keyboard only, Narrator | Tested by the maintainer with Hash Tool 1.2 (November 2026). The 1.2.3 fixes were checked with automated tests and with the UI Automation tree that screen readers read. |
| Windows 10 and 11 | NVDA, JAWS | Expected to work, since they read the same UI Automation tree as Narrator. Not tested regularly; reports are welcome. |
| macOS (Apple silicon and Intel) | VoiceOver | Not tested yet. Avalonia exposes the app to macOS accessibility. |
| Linux (x64 AppImage) | Orca | Not tested yet. Avalonia's AT-SPI support is newer and may be incomplete. |
| Any | Terminal with a screen reader or braille display | The command-line tool prints plain text. |

## Known limitations

These are known problems, with a workaround where one exists. Please report anything else.

- **Progress is not read aloud during a run.** Each row shows its status as text, and a run with several files ends
  with a summary dialog. After a single file, move to its row to hear the result.
- **The app does not follow the system's contrast themes** (Windows contrast themes, macOS *Increase contrast*). Choose
  *Settings → Theme style → High Contrast*. Standard controls such as buttons and the focus rectangle keep the colours
  of the Fluent theme.
- **The app does not follow the system text size.** Use *Font size* and *UI scale* in Settings.
- **There is no "reduce motion" setting.** The only animation is the theme colour fade.
- **The Developer console uses fixed colours** (light text on a dark background) in every theme.
- **The Glassmorphism style and the translucent window background** (Windows and macOS) depend on what is behind the
  window. Pick another style if text is hard to read.
- **Tooltips appear on mouse hover only.** On Verify, the computed hash is only in the tooltip of the status; Generate
  shows it in a text box you can focus and copy.
- **Dialogs cannot be moved with the keyboard.** They open centred over the main window.
- **Hashes and paths are not forced left-to-right** in right-to-left layouts, so they may be displayed in the wrong
  direction.
- **Shortcuts in tooltips say Ctrl on macOS too.** Use ⌘.
- **Most translations were made with AI assistance**, including the names screen readers announce. Corrections from
  native speakers are welcome.
- **The command-line tool is in English only.**
- **VoiceOver and Orca have not been tested.**

## Reporting an accessibility problem

Open an issue with the
[Accessibility issue form](https://github.com/poli0981/HashTool/issues/new?template=accessibility.yml). If the form is
hard to use, a plain issue or a post in [Discussions](https://github.com/poli0981/HashTool/discussions) is fine too.

Helpful details:

- what you were trying to do, what you expected and what happened;
- the assistive technology and its version (for example NVDA 2026.3, or "keyboard only");
- your operating system and the Hash Tool version (shown on the About page);
- the steps to get there. A screenshot or recording helps but is optional.

You do not need to tell me about any disability. Security problems go through [SECURITY.md](SECURITY.md) instead.

### Severity

| Severity | Meaning | Example |
|---|---|---|
| Critical | You cannot generate or verify a hash at all. | A screen reader cannot reach **COMPUTE ALL**. |
| High | A task works only with a workaround or with great effort. | A setting can only be changed with the mouse. |
| Medium | Annoying or inconsistent, but you can get it done. | A button is announced with the wrong name. |
| Low | Minor or cosmetic. | A heading level is off. |

### What happens next

Hash Tool is maintained by one person, so there is no fixed response time. I read every report and reply as soon as I
can. Critical and High problems come before new features; if a fix takes a while, I will suggest a workaround. Fixes are
listed in [CHANGELOG.md](CHANGELOG.md) and the release notes, with credit if you would like it. If you are willing, I may
ask you to try a fix before it is released.

## For contributors

When you change the UI:

- Give every control that takes focus a translated name: `AutomationProperties.Name="{Binding Localization[Key]}"`.
  This matters for icon buttons and for buttons whose content is a panel, which Avalonia otherwise announces by the
  panel's type name ("Avalonia.Controls.StackPanel").
- Make new controls reachable with Tab and usable with Enter, Space and the arrow keys. A control that hides itself
  while focused hands the focus on with `controls:FocusHandOff.Target`; lists with controls in their rows use
  `controls:RowTabNavigation.IsEnabled`.
- Never use colour as the only signal. New colours in `ThemePalettes` keep 4.5:1 for text and 3:1 for icons.
- Page titles use `AutomationProperties.HeadingLevel="1"`; section titles use the `sectionTitle` class.
- Add new text to every language with `python -I eng/loc.py set` (see [CONTRIBUTING.md](CONTRIBUTING.md)).

Testing:

- **Automated:** `tests/HashTool.App.Tests/AccessibilityTests.cs` checks names on every page in two languages, headings,
  Tab inside file rows, focus when buttons swap, and the contrast of every theme. CI runs it on Windows, macOS and Linux.
- **By hand, for UI changes:** one pass with the keyboard only, a quick check with a screen reader (Narrator:
  Win+Ctrl+Enter; VoiceOver: ⌘+F5; Orca on Linux), UI scale 2.0, and the High Contrast style.
  [Accessibility Insights for Windows](https://accessibilityinsights.io/docs/windows/overview/) shows what screen
  readers see.

## Ownership and review

The maintainer, [poli0981](https://github.com/poli0981), is responsible for accessibility: triaging reports, keeping
this page up to date, and checking new releases. This statement is reviewed with every minor release and whenever a
report changes it.

## Feedback

Suggestions about this statement or these practices are welcome as an issue or a pull request. If something blocks you
right now, use [Reporting an accessibility problem](#reporting-an-accessibility-problem).

This statement follows the
[Open Source Accessibility Toolkit template](https://github.com/open-source-accessibility/accessibility-toolkit/blob/main/ACCESSIBILITY.md)
and the [accessibility best practices](https://opensource.guide/accessibility-best-practices-for-your-project/) of Open
Source Guides.

© 2026 poli0981. All rights reserved.
