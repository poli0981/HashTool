using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HashTool.Core.Hashing;

namespace HashTool.Core.Checksums;

public enum ChecksumFormat
{
    /// <summary>GNU coreutils: <c>hash  name</c> or <c>hash *name</c>.</summary>
    Gnu,

    /// <summary>BSD / <c>--tag</c> style: <c>SHA256 (name) = hash</c>.</summary>
    Bsd,

    /// <summary>Simple File Verification: <c>name CRC32</c>.</summary>
    Sfv,

    /// <summary>Only a hash, without a file name (classic Hash Tool sidecar, certutil output).</summary>
    Bare
}

/// <summary>One line of a checksum file.</summary>
/// <param name="Hash">Normalised hexadecimal digest.</param>
/// <param name="FileName">Path as written in the file (may be relative); <c>null</c> for bare hashes.</param>
/// <param name="Algorithm">Algorithm, or <c>null</c> if it could not be determined.</param>
/// <param name="IsAlgorithmGuessed">True when the algorithm was inferred from the digest length alone.</param>
public sealed record ChecksumEntry(string Hash, string? FileName, HashType? Algorithm, bool IsAlgorithmGuessed, int LineNumber)
{
    /// <summary>
    /// Resolves <see cref="FileName"/> against the directory that contains the checksum file. Returns <c>null</c> for
    /// network (UNC) and device paths: verifying a downloaded checksum file must never make the app connect to a remote
    /// share (which would leak NTLM credentials on Windows) or read a device.
    /// </summary>
    public string? ResolvePath(string baseDirectory)
    {
        if (string.IsNullOrEmpty(FileName) || !IsSafePath(FileName)) return null;
        var relative = FileName.Replace('\\', '/');
        if (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(FileName) ? FileName : Path.Combine(baseDirectory, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return IsSafePath(full) ? full : null;
    }

    /// <summary>False for UNC paths (<c>\\server\share</c>, <c>//server</c>), device paths and special file systems.</summary>
    public static bool IsSafePath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
        if (!OperatingSystem.IsWindows() &&
            (path.StartsWith("/dev/", StringComparison.Ordinal) || path.StartsWith("/proc/", StringComparison.Ordinal) ||
             path.StartsWith("/sys/", StringComparison.Ordinal)))
            return false;
        return path.IndexOf('\0') < 0;
    }
}

/// <summary>A parsed checksum file.</summary>
public sealed class ChecksumDocument(IReadOnlyList<ChecksumEntry> entries, IReadOnlyList<int> invalidLines, ChecksumFormat format)
{
    public IReadOnlyList<ChecksumEntry> Entries { get; } = entries;

    /// <summary>1-based numbers of lines that are neither comments nor valid entries.</summary>
    public IReadOnlyList<int> InvalidLines { get; } = invalidLines;

    /// <summary>Format of the first entry (files rarely mix formats).</summary>
    public ChecksumFormat Format { get; } = format;

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>
    /// Finds the entry describing <paramref name="filePath"/>: by resolved path first, then by file name. A file with a
    /// single bare hash matches any file.
    /// </summary>
    public ChecksumEntry? FindEntry(string filePath, string baseDirectory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var entry in Entries)
        {
            var resolved = entry.ResolvePath(baseDirectory);
            if (resolved is not null && string.Equals(resolved, fullPath, comparison)) return entry;
        }

        var fileName = Path.GetFileName(fullPath);
        var byName = Entries.FirstOrDefault(e =>
            e.FileName is not null && string.Equals(Path.GetFileName(e.FileName.Replace('\\', '/')), fileName, comparison));
        if (byName is not null) return byName;

        return Entries.Count == 1 && Entries[0].FileName is null ? Entries[0] : null;
    }
}
