# Using HashTool

## The app

### Generate Hash

1. Drop files or folders on the page, or use **Add Files…** (Ctrl/⌘+O) / **Add Folder…** (Ctrl/⌘+Shift+O).
2. Pick the **Algorithm** for new rows (each row can be changed until it runs).
3. **COMPUTE ALL** (Ctrl/⌘+Enter) or the ▶ button on a row. **Cancel** (Esc) stops everything; ■ stops one row.
4. Use the result:
   - **Export → Save Checksum File…** writes one file for everything — GNU (`sha256sum` format, e.g. `SHA256SUMS`),
     BSD tagged, SFV (CRC32 only), JSON or CSV. Paths are relative to where you save it, so keep it next to the files.
   - The 💾 button saves one row as `file.ext.sha256` next to the file.
   - **Export → Copy All Hashes** copies the list; **Compress (.Zip)** bundles the hash files and a `CHECKSUMS.txt`.

### Verify Hash

- **Checksum file** — drop it or use **Open Checksum File…**. Every file it lists is added with its expected hash and
  verified relative to the checksum file's folder. Supported: `sha256sum`/`md5sum`/`b3sum` output (also with `*name`
  and escaped names), BSD tagged lines (`SHA256 (name) = hash`), `.sfv`, xxhsum (`XXH3_` prefix), and files that contain
  only a hash (`image.iso.sha256`).
- **A file** — drop it; HashTool looks for a sidecar (`image.iso.sha256`, `.md5`, `.b3`…). Otherwise paste the
  expected hash, drop a hash file onto the row's hash box, or browse for one.
- **Default algorithm** is used when neither the checksum file nor the hash length decides (64 hex digits can be
  SHA-256 or BLAKE3, for example).

Results: **VALID**, **MISMATCH!**, *Not a valid … hash* (wrong length or characters), *Original file missing*,
*Access denied*, *In use by another program*, *Timed out*. A CRC32 written by Hash Tool 1.1 (reversed byte order) shows
**VALID (old CRC32 byte order)**.

On Windows, files you cannot read can be retried with **Restart as Administrator** (offered after a run, and in
Settings). While elevated, Windows blocks drag and drop from Explorer.

### Settings worth knowing

| Setting | What it does |
|---|---|
| Files processed at the same time | 0 = automatic (up to 4). Large files (≥ 1 GiB) always run one at a time. |
| Optimize for hard drives | One file at a time — faster on spinning disks and network shares. |
| Include hidden files | Also add hidden/system files when scanning folders. |
| Input file size limit / Max files per folder scan / Max folders per click | Guards against huge drops. |
| File processing timeout | Gives up on a single file after the time you set. |
| Hash letter case | Upper (default) or lower case on screen and in single-hash files; checksum files use lower case like `sha256sum`. |
| Mask hash values | Hides hashes on screen until you reveal a row (stays editable while settings are locked). |
| Explorer context menu (Windows) | Adds *Generate hash with Hash Tool* and *Verify with Hash Tool* (Windows 11: *Show more options*). |
| Check for updates automatically | At most once a day at start-up. |

Settings and logs live in `%LOCALAPPDATA%\HashTool` (Windows), `~/Library/Application Support/HashTool` (macOS) or
`~/.local/share/HashTool` (Linux); *Settings → Configuration → Open Data Folder* opens it.

### Command-line arguments

```text
CheckHash [--create | --verify] [--files-from list.txt] [paths...]
```

Paths open on Generate (`--create`) or Verify (`--verify`); without a verb, checksum files go to Verify and everything
else to Generate. If Hash Tool is already open, the files are handed to that window. On macOS, use *Open With* in
Finder; on Linux, pass paths to the AppImage.

`CheckHash --smoke-test [--smoke-out result.json] [--expect-version X.Y.Z]` starts the app without a window, checks
every page and language, hashes and verifies a file, and exits with 0 when everything works.

## The `hashtool` command-line tool

```text
hashtool hash <paths>... [-a ALGORITHM...] [-r] [--hidden] [-f gnu|bsd|sfv|json|csv] [-o FILE] [-u] [-j N]
hashtool verify <checksum-files>... [-a ALGORITHM] [-q] [--ignore-missing]
hashtool check <file> -e HASH [-a ALGORITHM]
hashtool algorithms
```

| Option | Meaning |
|---|---|
| `-a, --algorithm` | `sha256` (default), `sha1`, `md5`, `sha384`, `sha512`, `blake3`, `xxhash32`, `xxhash64`, `xxhash3`, `xxhash128`, `crc32`. Repeat or separate with commas: `-a sha256,blake3`. |
| `-r, --recursive` | Hash every file inside the given folders (links are not followed). |
| `-f, --format` | Output format. Default: `gnu` for one algorithm, `bsd` for several. |
| `-o, --output` | Write to a file; paths in it are relative to its folder. |
| `-u, --uppercase` | Upper-case hexadecimal digits. |
| `-j, --jobs` | Files hashed in parallel (0 = automatic, 1 = one at a time). |
| `-q, --quiet` | `verify`: only print problems. |
| `--ignore-missing` | `verify`: skip listed files that do not exist. |

Exit codes: **0** everything OK · **1** a file could not be read or did not match · **2** usage error or unreadable
checksum file.

Examples:

```text
hashtool hash -r . -o SHA256SUMS && hashtool verify SHA256SUMS
hashtool hash -a crc32 -f sfv *.rar > files.sfv
hashtool check ubuntu.iso -e 3b1c…               # algorithm guessed from the hash length
```
