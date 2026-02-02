# Changelog

All notable changes to this project will be documented in this file.

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
