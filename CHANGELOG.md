# Changelog

All notable changes to this project will be documented in this file.

## [1.2.2] - 2026-10-26

A security update: the app and the command-line tool now ship with the .NET 10.0.14 runtime.

### Security
- Updated to the .NET 10.0.14 runtime (out-of-band security release with fixes for the Brotli decoder, CVE-2026-111497, and network sockets, CVE-2026-70197), which the self-contained downloads include.

### Changed
- Updated Microsoft.Extensions, System.IO.Hashing and System.CommandLine to their 10.0.14 servicing releases, and the build and analysis tools to their latest versions.

---

## [1.2.1] - 2026-10-25

A small maintenance release: two fixes found while adding several hundred tests, and code analysis on every change.

### Fixed
- Verify: a hash file dropped onto a row's hash box fills in that row's expected hash again, instead of being added to the list as one more file.
- ZIP export: files from folders whose names share a beginning (such as `photos` and `photos-2024`) keep their relative paths instead of being stored with absolute ones.

### Security
- Every pull request and `main` are analysed by SonarCloud, including test coverage, and Dependabot now also keeps the build tools (vpk, licence and SBOM tools) up to date.

---

## [1.2.0] - 2026-10-15

A big rework under the hood: faster hashing, standard checksum files, a command-line tool, a Linux build and a long list of fixes.

### Added
- **Checksum files**: save all hashes as one `SHA256SUMS`-style file (GNU, BSD tagged, SFV, JSON or CSV), and verify any checksum file: `sha256sum`/`md5sum` output, BSD tagged lists, `.sfv`, xxhsum (`XXH3_` prefix) and single-hash files such as `image.iso.sha256`. Dropping a checksum file on Verify loads every file it lists.
- **Command-line tool `hashtool`** for Windows, macOS and Linux: `hashtool hash`, `hashtool verify` (like `sha256sum -c`), `hashtool check` and `hashtool algorithms`.
- **Linux** (AppImage) and **Intel Mac** builds.
- **Open files from the system**: optional Explorer context menu on Windows ("Generate hash with Hash Tool", "Verify with Hash Tool"), "Open With" on macOS and `--create`/`--verify` command-line arguments. A second launch hands its files to the window that is already open.
- Settings for files processed at the same time, hard-drive mode, hidden files, hash letter case, default checksum file format and the automatic update check.
- Updates: optional automatic check (at most once a day), release notes, cancellable download, install now or when closing the app.
- "Restart as Administrator" (Windows) when files cannot be read, keeping the file list.
- Developer console: level filter, auto-scroll and export.
- Keyboard shortcuts: Ctrl/⌘+O (add files), Ctrl/⌘+Shift+O (add folder), Ctrl/⌘+F (search), Ctrl/⌘+Enter (run all), Esc (cancel), Delete (remove).
- The window remembers its size and position.

### Changed
- **CRC32 is shown in the standard byte order** used by 7-Zip, SFV files and `cksum`: "123456789" now gives `CBF43926` (1.1 showed `2639F4CB`). CRC32 values saved by 1.1 still verify and are marked as using the old byte order.
- New hashing engine: each file is read once for all selected algorithms, with large double-buffered reads, and several files are hashed at once (large files in their own lane). In benchmarks SHA-256 of a large file is about 1.7× faster than a plain single-buffer read, and 2,000 small files finish about 3.5× faster than one at a time.
- Adding folders scans in the background, skips unreadable folders and symbolic-link loops, and no longer freezes the window.
- Settings are written safely; a damaged settings file is backed up and reset instead of breaking the app. The chosen language is saved right away.
- Daily log files with ISO timestamps (kept 14 days), user folder names masked, and crash reports.
- Statuses, filters and dialogs follow the selected language immediately, including right-to-left layouts. Automatic language detection picks the right variant (Traditional Chinese for Taiwan and Hong Kong, Latin American Spanish, Norwegian, British English for Australia and others).
- The colour-blind and high-contrast themes also recolour statuses.
- Updated to Avalonia 12, the .NET 10.0.13 runtime, Velopack 1.2 and current versions of every library.

### Fixed
- Crash when dropping a folder.
- The app closing on unexpected errors, such as saving into a read-only folder; errors are now shown in a dialog.
- Single-letter shortcuts (Shift+A/C/Z) firing while typing.
- The search highlight showing the end of a file name twice.
- Split archives (`.001`, `.7z.002`) being mistaken for hash files.
- "Invalid" when loading a hash file that does not name the file, such as `SHA256SUMS`.
- The expected hash getting lost when a file and its hash file were dropped together.
- Calendar and number formatting in logs for some languages (Thai, Arabic, Turkish).
- Missing, untranslated or misaligned text in several dialogs and languages; the Filipino translation is complete.

### Removed
- The Free Memory button, Force Quit and its timeout setting.
- Starting as administrator automatically (replaced by "Restart as Administrator" when it is needed).
- The developer (pre-release) update channel and its disclaimer.

### Security
- Removed the vulnerable Tmds.DBus.Protocol version (GHSA-xrw6-gwf8-vvr9) through the Avalonia upgrade.
- Checksum files cannot point to network (UNC) or device paths, and exported CSV files cannot inject spreadsheet formulas.
- Releases are built and tested in GitHub Actions, with `SHA256SUMS`, SBOMs and build provenance attestations; every pull request is scanned for vulnerable packages, licences and secrets.

---

## [1.1.3] - 2026-02-23

### Removed
- Rollback feature


## [1.1.1] - 2026-02-22

### Fixed
- Cyprus language name now displays fully (tested ✅)
- Rollback to previous version now works correctly (tested ✅)
- Language switching (auto + manual) now works on macOS (tested ✅)

### Changed
- Release Notes in new versions are now much easier to read (not fully tested yet :D)

Quick hotfix to make things smoother. Enjoy.

---

## [1.1.0] - 2026-02-20

### Added
**New Hash Algorithms**
- xxHash32, xxHash64, xxHash3, xxHash128
- CRC32

**New Features**
- Cancel button to abort the entire hashing process
- "Add Folder" – add all files from one or more folders at once
- Advanced search (by Name, Status, and File Size)
- "Clear Hash" button + individual clear icon for each file
- "Clear Failed" button to remove unprocessed/failed/cancelled items
- "Free up Memory" button in Settings
- Configurable input file limit (default: 1000, toggleable)
- Configurable input folder limit (default: 3, toggleable)
- Real-time disk read/write speed display
- New UI themes: Retro, Glassmorphism, Cyberpunk
- "Documents" section in the About page
- More detailed progress counter (e.g. 15/100 files processed)
- Missing third-party libraries added to Acknowledgements (sorry for the earlier miss 😅)
- Donate section

**Performance**
- Multi-threading: now runs up to 3 hashing threads simultaneously for faster processing
- Optimized dev-mode logging

### Changed / UI/UX Improvements
- Added tooltips in many places
- Improved MessageBox notifications
- Better individual file item UI in the list
- Language change now shows confirmation dialog (plus a few more confirmation boxes)
- Removed MicaCustom theme

### Fixed
- Dark/Light mode switching logic
- Hash creation & verification logic issues
- Incomplete translations in some languages
- Language loading, switching and default setting bugs
- Application update checking logic
- "Reset Theme" button renamed to "Reset Settings"
- Miscellaneous small bugs

### Other
- Thank you for **26 downloads** on v1.0.0.
- Hope you enjoy this update. Have a nice day.

---

## [1.0.0] - 2026-02-03 GMT + 7 (Initial Release)

### Features

#### Create Hash
- **Multi-Algorithm Support**: Calculate hashes using MD5, SHA1, SHA256, SHA384, SHA512, and BLAKE3.
- **Batch Processing**: Compute hashes for multiple files simultaneously with high performance.
- **File Compression**: Create ZIP archives of the generated hash files.
- **Export**: Save hash results to separate files (e.g., `.sha256`).
- **Clipboard Integration**: Quickly copy hash values to the clipboard.

#### Check Hash
- **Verification**: Verify file integrity by comparing against expected hash values.
- **Smart Loading**: Load hash files (like `.md5`) to automatically identify and verify the corresponding source files.
- **Sidecar Detection**: Automatically detects adjacent hash files for simplified verification.
- **Drag & Drop**: Seamlessly add files or hash lists via drag and drop.

#### Localization
- **Global Support**: Localized in over 50 languages, including English, Spanish, French, German, Chinese, Japanese, and more.
- **RTL Support**: Full support for Right-to-Left languages such as Arabic and Hebrew.

#### Settings & Customization
- **Theming**: Choose between Light, Dark, or System theme variants.
- **Typography**: Customize font family, base size, and UI scaling.
- **Privacy**: "Hash Masking" option to hide hash characters in the UI.
- **Advanced Control**:
    - Admin Mode (Windows) for privileged file access.
    - File size limits to prevent processing accidentally large files.
    - Configurable timeouts for file operations.
    - Developer Mode for extended logging.

#### Technical
- **Cross-Platform**: Built with Avalonia UI, running on Windows, macOS, and Linux.
- **Performance**: Optimized for multi-core processors using `Parallel.ForEachAsync` and efficient memory usage with `ArrayPool`.
