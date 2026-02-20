# Changelog

All notable changes to this project will be documented in this file.

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
- **File Compression**: Create ZIP archives containing both the original files and their generated hash files.
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
