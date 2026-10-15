using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace HashTool.Core.Hashing;

/// <summary>Metadata about the supported algorithms: sizes, names, file extensions.</summary>
public static class HashAlgorithms
{
    /// <summary>All algorithms, in the order shown to users.</summary>
    public static IReadOnlyList<HashType> All { get; } = Enum.GetValues<HashType>();

    /// <summary>
    /// Preference order used when a hash's length alone is ambiguous (e.g. 64 hex characters can be SHA-256 or BLAKE3).
    /// </summary>
    private static readonly HashType[] GuessPriority =
    [
        HashType.SHA256, HashType.SHA1, HashType.MD5, HashType.SHA512, HashType.SHA384, HashType.CRC32,
        HashType.XxHash64, HashType.BLAKE3, HashType.XxHash128, HashType.XxHash3, HashType.XxHash32
    ];

    private static readonly FrozenDictionary<string, HashType> Aliases = new Dictionary<string, HashType>(StringComparer.OrdinalIgnoreCase)
    {
        ["md5"] = HashType.MD5,
        ["sha1"] = HashType.SHA1,
        ["sha-1"] = HashType.SHA1,
        ["sha256"] = HashType.SHA256,
        ["sha-256"] = HashType.SHA256,
        ["sha2-256"] = HashType.SHA256,
        ["sha384"] = HashType.SHA384,
        ["sha-384"] = HashType.SHA384,
        ["sha512"] = HashType.SHA512,
        ["sha-512"] = HashType.SHA512,
        ["blake3"] = HashType.BLAKE3,
        ["b3"] = HashType.BLAKE3,
        ["xxh32"] = HashType.XxHash32,
        ["xxhash32"] = HashType.XxHash32,
        ["xxh64"] = HashType.XxHash64,
        ["xxhash64"] = HashType.XxHash64,
        ["xxh3"] = HashType.XxHash3,
        ["xxh3_64"] = HashType.XxHash3,
        ["xxhash3"] = HashType.XxHash3,
        ["xxh128"] = HashType.XxHash128,
        ["xxh3_128"] = HashType.XxHash128,
        ["xxhash128"] = HashType.XxHash128,
        ["crc32"] = HashType.CRC32,
        ["crc-32"] = HashType.CRC32,
        ["sfv"] = HashType.CRC32
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Digest size in bytes.</summary>
    public static int SizeInBytes(HashType type) => type switch
    {
        HashType.MD5 => 16,
        HashType.SHA1 => 20,
        HashType.SHA256 => 32,
        HashType.SHA384 => 48,
        HashType.SHA512 => 64,
        HashType.BLAKE3 => 32,
        HashType.XxHash32 => 4,
        HashType.XxHash64 => 8,
        HashType.XxHash3 => 8,
        HashType.XxHash128 => 16,
        HashType.CRC32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Length of the digest written as hexadecimal.</summary>
    public static int HexLength(HashType type) => SizeInBytes(type) * 2;

    /// <summary>MD5 and SHA-1 are broken for collision resistance.</summary>
    public static bool IsInsecure(this HashType type) => type is HashType.MD5 or HashType.SHA1;

    /// <summary>Whether the value is a defined member of <see cref="HashType"/>.</summary>
    public static bool IsDefined(HashType type) => Enum.IsDefined(type);

    /// <summary>
    /// Extension used for sidecar files (<c>file.iso.sha256</c>). Matches what Hash Tool 1.x wrote.
    /// </summary>
    public static string FileExtension(HashType type) => type switch
    {
        HashType.MD5 => "md5",
        HashType.SHA1 => "sha1",
        HashType.SHA256 => "sha256",
        HashType.SHA384 => "sha384",
        HashType.SHA512 => "sha512",
        HashType.BLAKE3 => "blake3",
        HashType.XxHash32 => "xxhash32",
        HashType.XxHash64 => "xxhash64",
        HashType.XxHash3 => "xxhash3",
        HashType.XxHash128 => "xxhash128",
        HashType.CRC32 => "crc32",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Tag used by BSD-style checksum lines, e.g. <c>SHA256 (file) = …</c>.</summary>
    public static string BsdTag(HashType type) => type switch
    {
        HashType.MD5 => "MD5",
        HashType.SHA1 => "SHA1",
        HashType.SHA256 => "SHA256",
        HashType.SHA384 => "SHA384",
        HashType.SHA512 => "SHA512",
        HashType.BLAKE3 => "BLAKE3",
        HashType.XxHash32 => "XXH32",
        HashType.XxHash64 => "XXH64",
        HashType.XxHash3 => "XXH3",
        HashType.XxHash128 => "XXH128",
        HashType.CRC32 => "CRC32",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Parses an algorithm name such as <c>sha256</c>, <c>SHA-256</c>, <c>xxh64</c> or <c>b3</c>.
    /// Numbers are rejected (unlike <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>).
    /// </summary>
    public static bool TryParse(string? name, out HashType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim().TrimStart('.');
        if (Aliases.TryGetValue(name, out type)) return true;
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Algorithms whose hexadecimal digest has the given length, most likely first.</summary>
    public static IReadOnlyList<HashType> CandidatesForHexLength(int hexLength) =>
        GuessPriority.Where(t => HexLength(t) == hexLength).ToArray();
}
