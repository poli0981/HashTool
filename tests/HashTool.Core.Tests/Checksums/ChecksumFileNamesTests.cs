using System.Linq;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Checksums;

public sealed class ChecksumFileNamesTests
{
    [Theory]
    [InlineData("image.iso.sha256", HashType.SHA256)]
    [InlineData("image.iso.SHA256SUM", HashType.SHA256)]
    [InlineData("x.md5", HashType.MD5)]
    [InlineData("x.b3", HashType.BLAKE3)]
    [InlineData("x.blake3", HashType.BLAKE3)]
    [InlineData("x.xxhash64", HashType.XxHash64)]
    [InlineData("release.sfv", HashType.CRC32)]
    [InlineData("SHA256SUMS", HashType.SHA256)]
    [InlineData("sha512sums.txt", HashType.SHA512)]
    public void RecognisesChecksumFiles(string name, HashType expected)
    {
        Assert.True(ChecksumFileNames.IsChecksumFile(name, out var algorithm));
        Assert.Equal(expected, algorithm);
    }

    [Fact]
    public void GenericChecksumsFile_HasNoAlgorithmHint()
    {
        Assert.True(ChecksumFileNames.IsChecksumFile("/dl/CHECKSUMS.txt", out var algorithm));
        Assert.Null(algorithm);
    }

    [Theory]
    [InlineData("movie.part.001")]
    [InlineData("archive.7")]
    [InlineData("notes.txt")]
    [InlineData("photo.png")]
    [InlineData("")]
    public void IgnoresOtherFiles(string name) => Assert.False(ChecksumFileNames.IsChecksumFile(name, out _));

    [Fact]
    public void SidecarTarget_StripsTheChecksumExtension()
    {
        Assert.Equal("/data/image.iso", ChecksumFileNames.SidecarTarget("/data/image.iso.sha256"));
        Assert.Null(ChecksumFileNames.SidecarTarget("/data/image.iso"));
        Assert.Null(ChecksumFileNames.SidecarTarget("/data/.sha256"));
    }

    [Fact]
    public void SidecarCandidates_PutPreferredAlgorithmFirst()
    {
        var candidates = ChecksumFileNames.SidecarCandidates("/d/f.bin", HashType.MD5).ToList();

        Assert.Equal(HashType.MD5, candidates[0].Algorithm);
        Assert.Contains(("/d/f.bin.sha256", HashType.SHA256), candidates);
        Assert.Equal("/d/f.bin.xxhash3", ChecksumFileNames.SidecarPath("/d/f.bin", HashType.XxHash3));
    }
}
