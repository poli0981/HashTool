using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using HashTool.Core.Hashing;

namespace HashTool.Core.Checksums;

/// <summary>Recognises checksum files by name and finds sidecar files next to a data file.</summary>
public static class ChecksumFileNames
{
    private static readonly FrozenDictionary<string, HashType> Extensions = new Dictionary<string, HashType>(StringComparer.OrdinalIgnoreCase)
    {
        [".md5"] = HashType.MD5,
        [".md5sum"] = HashType.MD5,
        [".sha1"] = HashType.SHA1,
        [".sha1sum"] = HashType.SHA1,
        [".sha256"] = HashType.SHA256,
        [".sha256sum"] = HashType.SHA256,
        [".sha384"] = HashType.SHA384,
        [".sha384sum"] = HashType.SHA384,
        [".sha512"] = HashType.SHA512,
        [".sha512sum"] = HashType.SHA512,
        [".blake3"] = HashType.BLAKE3,
        [".b3"] = HashType.BLAKE3,
        [".b3sum"] = HashType.BLAKE3,
        [".xxhash32"] = HashType.XxHash32,
        [".xxh32"] = HashType.XxHash32,
        [".xxhash64"] = HashType.XxHash64,
        [".xxh64"] = HashType.XxHash64,
        [".xxhash3"] = HashType.XxHash3,
        [".xxh3"] = HashType.XxHash3,
        [".xxhash128"] = HashType.XxHash128,
        [".xxh128"] = HashType.XxHash128,
        [".crc32"] = HashType.CRC32,
        [".sfv"] = HashType.CRC32
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, HashType?> WellKnownNames = new Dictionary<string, HashType?>(StringComparer.OrdinalIgnoreCase)
    {
        ["MD5SUMS"] = HashType.MD5,
        ["SHA1SUMS"] = HashType.SHA1,
        ["SHA256SUMS"] = HashType.SHA256,
        ["SHA384SUMS"] = HashType.SHA384,
        ["SHA512SUMS"] = HashType.SHA512,
        ["B3SUMS"] = HashType.BLAKE3,
        ["BLAKE3SUMS"] = HashType.BLAKE3,
        ["CHECKSUMS"] = null,
        ["CHECKSUM"] = null
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="path"/> looks like a checksum file (<c>*.sha256</c>, <c>SHA256SUMS</c>, <c>*.sfv</c>…).
    /// <paramref name="algorithm"/> is the algorithm implied by the name, or <c>null</c> when only the content can tell.
    /// </summary>
    public static bool IsChecksumFile(string path, out HashType? algorithm)
    {
        algorithm = null;
        if (string.IsNullOrEmpty(path)) return false;

        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(fileName);
        if (extension.Length > 1 && Extensions.TryGetValue(extension, out var byExtension))
        {
            algorithm = byExtension;
            return true;
        }

        var stem = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
        if (WellKnownNames.TryGetValue(stem, out var byName))
        {
            algorithm = byName;
            return true;
        }

        return false;
    }

    /// <summary>The data file a sidecar such as <c>image.iso.sha256</c> belongs to, or <c>null</c>.</summary>
    public static string? SidecarTarget(string sidecarPath)
    {
        var extension = Path.GetExtension(sidecarPath);
        if (extension.Length <= 1 || !Extensions.ContainsKey(extension)) return null;
        var target = sidecarPath[..^extension.Length];
        return Path.GetFileName(target).Length > 0 ? target : null;
    }

    /// <summary>Sidecar path Hash Tool writes for a file, e.g. <c>image.iso.sha256</c>.</summary>
    public static string SidecarPath(string filePath, HashType algorithm) =>
        $"{filePath}.{HashAlgorithms.FileExtension(algorithm)}";

    /// <summary>Every sidecar name that may hold the hash of <paramref name="filePath"/>, preferred algorithm first.</summary>
    public static IEnumerable<(string Path, HashType Algorithm)> SidecarCandidates(string filePath, HashType? preferred = null)
    {
        if (preferred is { } first)
        {
            foreach (var (extension, type) in Extensions)
            {
                if (type == first) yield return (filePath + extension, type);
            }
        }

        foreach (var (extension, type) in Extensions)
        {
            if (type != preferred) yield return (filePath + extension, type);
        }
    }
}
