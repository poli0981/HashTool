using System;
using System.Buffers.Binary;
using System.Globalization;

namespace HashTool.Core.Hashing;

public enum HashMatch
{
    /// <summary>The expected value equals the computed digest.</summary>
    Match,

    /// <summary>
    /// CRC-32 written in the reversed (little-endian) byte order that Hash Tool 1.x produced. The file is intact.
    /// </summary>
    LegacyByteOrderMatch,

    Mismatch,

    /// <summary>The expected value has the wrong length for the algorithm (probably a different algorithm).</summary>
    LengthMismatch,

    /// <summary>The expected value is empty or not hexadecimal.</summary>
    InvalidExpected
}

/// <summary>Compares user-supplied hashes with computed digests.</summary>
public static class HashMatcher
{
    /// <summary>
    /// Normalises a pasted hash: trims it, drops whitespace, a <c>0x</c> or xxhsum's <c>XXH3_</c> prefix.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var span = value.AsSpan().Trim();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) span = span[2..];
        if (span.StartsWith("XXH3_", StringComparison.OrdinalIgnoreCase)) span = span[5..];

        Span<char> buffer = span.Length <= 256 ? stackalloc char[span.Length] : new char[span.Length];
        var length = 0;
        foreach (var c in span)
        {
            if (!char.IsWhiteSpace(c)) buffer[length++] = c;
        }

        return new string(buffer[..length]);
    }

    public static bool IsHex(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }

        return true;
    }

    public static HashMatch Compare(string? expected, HashResult actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var normalized = Normalize(expected);
        if (!IsHex(normalized)) return HashMatch.InvalidExpected;
        if (normalized.Length != HashAlgorithms.HexLength(actual.Algorithm)) return HashMatch.LengthMismatch;

        var actualHex = actual.ToHex();
        if (string.Equals(normalized, actualHex, StringComparison.OrdinalIgnoreCase)) return HashMatch.Match;

        if (actual.Algorithm == HashType.CRC32 &&
            uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var legacy) &&
            BinaryPrimitives.ReverseEndianness(legacy) == BinaryPrimitives.ReadUInt32BigEndian(actual.Digest.Span))
        {
            return HashMatch.LegacyByteOrderMatch;
        }

        return HashMatch.Mismatch;
    }
}
