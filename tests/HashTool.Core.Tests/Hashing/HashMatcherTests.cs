using System.Linq;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Hashing;

public sealed class HashMatcherTests
{
    private static HashResult Crc(string hex) => new(HashType.CRC32, System.Convert.FromHexString(hex));

    [Theory]
    [InlineData("  CBF43926 ", "CBF43926")]
    [InlineData("0xcbf43926", "cbf43926")]
    [InlineData("cb f4 39 26", "cbf43926")]
    [InlineData("XXH3_78af5f94892f3950", "78af5f94892f3950")]
    [InlineData(null, "")]
    public void Normalize_StripsDecoration(string? input, string expected) =>
        Assert.Equal(expected, HashMatcher.Normalize(input));

    [Fact]
    public void Compare_IsCaseInsensitive() =>
        Assert.Equal(HashMatch.Match, HashMatcher.Compare("cbf43926", Crc("CBF43926")));

    [Fact]
    public void Compare_AcceptsCrc32WrittenByHashTool1x() =>
        Assert.Equal(HashMatch.LegacyByteOrderMatch, HashMatcher.Compare("2639F4CB", Crc("CBF43926")));

    [Fact]
    public void Compare_ReportsLengthMismatch() =>
        Assert.Equal(HashMatch.LengthMismatch, HashMatcher.Compare("d41d8cd98f00b204e9800998ecf8427e", Crc("CBF43926")));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public void Compare_RejectsGarbage(string expected) =>
        Assert.Equal(HashMatch.InvalidExpected, HashMatcher.Compare(expected, Crc("CBF43926")));

    [Fact]
    public void Compare_DetectsMismatch() =>
        Assert.Equal(HashMatch.Mismatch, HashMatcher.Compare("00000000", Crc("CBF43926")));

    [Theory]
    [InlineData("sha256", HashType.SHA256)]
    [InlineData("SHA-256", HashType.SHA256)]
    [InlineData(".b3", HashType.BLAKE3)]
    [InlineData("XXH3", HashType.XxHash3)]
    [InlineData("xxh128", HashType.XxHash128)]
    [InlineData("crc32", HashType.CRC32)]
    [InlineData("XxHash64", HashType.XxHash64)]
    public void TryParse_AcceptsCommonNames(string name, HashType expected)
    {
        Assert.True(HashAlgorithms.TryParse(name, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("001")]
    [InlineData("99")]
    [InlineData("zip")]
    [InlineData("")]
    public void TryParse_RejectsNumbersAndUnknownNames(string name) =>
        Assert.False(HashAlgorithms.TryParse(name, out _));

    [Fact]
    public void Candidates_PreferCommonAlgorithms()
    {
        Assert.Equal(HashType.SHA256, HashAlgorithms.CandidatesForHexLength(64)[0]);
        Assert.Equal(HashType.MD5, HashAlgorithms.CandidatesForHexLength(32)[0]);
        Assert.Equal(HashType.CRC32, HashAlgorithms.CandidatesForHexLength(8)[0]);
        Assert.Empty(HashAlgorithms.CandidatesForHexLength(10));
        Assert.All(HashAlgorithms.All, t => Assert.Contains(t, HashAlgorithms.CandidatesForHexLength(HashAlgorithms.HexLength(t))));
        Assert.Equal(HashAlgorithms.All.Count, HashAlgorithms.All.Distinct().Count());
    }
}
