using System;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Hashing;

public sealed class HashServiceTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("hashtool-tests-");
    private readonly HashService _service = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private string WriteFile(byte[] content, string name = "input.bin")
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Theory]
    [InlineData(HashType.MD5, "", "D41D8CD98F00B204E9800998ECF8427E")]
    [InlineData(HashType.MD5, "abc", "900150983CD24FB0D6963F7D28E17F72")]
    [InlineData(HashType.SHA1, "abc", "A9993E364706816ABA3E25717850C26C9CD0D89D")]
    [InlineData(HashType.SHA256, "", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData(HashType.SHA256, "abc", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData(HashType.SHA384, "abc",
        "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7")]
    [InlineData(HashType.SHA512, "abc",
        "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F")]
    [InlineData(HashType.BLAKE3, "", "AF1349B9F5F9A1A6A0404DEA36DCC9499BCB25C9ADC112B7CC9A93CAE41F3262")]
    [InlineData(HashType.BLAKE3, "abc", "6437B3AC38465133FFB63B75273A8DB548C558465D79DB03FD359C6CD5BD9D85")]
    [InlineData(HashType.XxHash32, "abc", "32D153FF")]
    [InlineData(HashType.XxHash64, "", "EF46DB3751D8E999")]
    [InlineData(HashType.XxHash64, "abc", "44BC2CF5AD770999")]
    [InlineData(HashType.XxHash3, "abc", "78AF5F94892F3950")]
    [InlineData(HashType.XxHash128, "abc", "06B05AB6733A618578AF5F94892F3950")]
    public async Task ComputesKnownAnswerVectors(HashType type, string input, string expected)
    {
        var path = WriteFile(Encoding.ASCII.GetBytes(input));

        var actual = await _service.ComputeHashAsync(path, type, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Crc32_CurrentlyEmitsLittleEndianBytes()
    {
        // Characterization of the 1.1.x behaviour: the conventional CRC-32 of "123456789" is CBF43926,
        // but the bytes come out reversed. A later commit fixes this.
        var path = WriteFile("123456789"u8.ToArray());

        var actual = await _service.ComputeHashAsync(path, HashType.CRC32, TestContext.Current.CancellationToken);

        Assert.Equal("2639F4CB", actual);
    }

    [Theory]
    [InlineData(HashType.SHA256, 4096)]
    [InlineData(HashType.XxHash64, 4096)]
    [InlineData(HashType.BLAKE3, 4096)]
    [InlineData(HashType.SHA256, null)]
    public async Task LargeFile_MatchesOneShotHash(HashType type, int? bufferSize)
    {
        var content = new byte[3 * 1024 * 1024 + 123];
        new Random(42).NextBytes(content);
        var path = WriteFile(content);

        var actual = await _service.ComputeHashAsync(path, type, TestContext.Current.CancellationToken, bufferSize);

        var expected = type switch
        {
            HashType.SHA256 => Convert.ToHexString(SHA256.HashData(content)),
            HashType.XxHash64 => Convert.ToHexString(XxHash64.Hash(content)),
            HashType.BLAKE3 => Convert.ToHexString(Blake3.Hasher.Hash(content).AsSpan()),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReportsProgressForEveryByte()
    {
        var content = new byte[1_000_000];
        var path = WriteFile(content);
        long total = 0;

        await _service.ComputeHashAsync(path, HashType.SHA256, TestContext.Current.CancellationToken, 64 * 1024,
            read => Interlocked.Add(ref total, read));

        Assert.Equal(content.Length, total);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var path = WriteFile(new byte[1024]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ComputeHashAsync(path, HashType.SHA256, cts.Token));
    }

    [Fact]
    public async Task MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(_dir.FullName, "missing.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ComputeHashAsync(path, HashType.SHA256, TestContext.Current.CancellationToken));
    }
}
