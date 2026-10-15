using System;

namespace HashTool.Core.Hashing;

/// <summary>Letter case used when a digest is written as hexadecimal.</summary>
public enum HexCase
{
    Upper,
    Lower
}

/// <summary>A computed digest, in the canonical byte order used by other tools (big-endian for CRC-32).</summary>
public sealed class HashResult(HashType algorithm, byte[] digest)
{
    public HashType Algorithm { get; } = algorithm;

    public ReadOnlyMemory<byte> Digest { get; } = digest;

    public string ToHex(HexCase letterCase = HexCase.Upper) =>
        letterCase == HexCase.Upper ? Convert.ToHexString(Digest.Span) : Convert.ToHexStringLower(Digest.Span);

    public override string ToString() => ToHex();
}
