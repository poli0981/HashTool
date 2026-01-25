## 2025-10-26 - [DoS via Unbounded File Read]
**Vulnerability:** Application read entire file content into memory for regex matching without size check in `CheckHashViewModel`.
**Learning:** `File.ReadAllTextAsync` on user-controlled paths (even "hash files") can lead to OOM/DoS if the file is unexpectedly large.
**Prevention:** Always check `FileInfo.Length` or use streams with limits when reading files, especially when expecting small files like hashes/configs.
