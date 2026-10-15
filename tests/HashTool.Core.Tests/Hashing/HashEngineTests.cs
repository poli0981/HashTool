using System;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Hashing;

public sealed class HashEngineTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static byte[] RandomBytes(int length, int seed = 7)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task MultipleAlgorithms_SingleRead_MatchIndividualResults()
    {
        var content = RandomBytes(2 * 1024 * 1024 + 17);
        var path = _temp.File("data.bin", content);
        var engine = new HashEngine(new HashEngineOptions { BufferSize = 64 * 1024 });
        long reported = 0;

        var results = await engine.ComputeFileAsync(path, HashAlgorithms.All,
            read => Interlocked.Add(ref reported, read), TestContext.Current.CancellationToken);

        Assert.Equal(content.Length, reported);
        Assert.Equal(HashAlgorithms.All, results.Select(r => r.Algorithm));
        foreach (var result in results)
        {
            var single = await engine.ComputeFileAsync(path, result.Algorithm, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(single.ToHex(), result.ToHex());
        }

        Assert.Equal(Convert.ToHexString(SHA512.HashData(content)), results.Single(r => r.Algorithm == HashType.SHA512).ToHex());
        Assert.Equal(Convert.ToHexString(XxHash128.Hash(content)), results.Single(r => r.Algorithm == HashType.XxHash128).ToHex());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(1024 * 1024)]
    public async Task BufferSizeDoesNotChangeTheResult(int bufferSize)
    {
        var content = RandomBytes(300_001);
        var path = _temp.File("data.bin", content);
        var engine = new HashEngine(new HashEngineOptions { BufferSize = bufferSize, Blake3ParallelThreshold = 1024 });

        var blake3 = await engine.ComputeFileAsync(path, HashType.BLAKE3, cancellationToken: TestContext.Current.CancellationToken);
        var sha256 = await engine.ComputeFileAsync(path, HashType.SHA256, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Convert.ToHexString(Blake3.Hasher.Hash(content).AsSpan()), blake3.ToHex());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), sha256.ToHex());
    }

    [Fact]
    public async Task Crc32_IsBigEndianLikeOtherTools()
    {
        var path = _temp.File("check.txt", "123456789"u8.ToArray());

        var result = await new HashEngine().ComputeFileAsync(path, HashType.CRC32, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cbf43926", result.ToHex(HexCase.Lower));
    }

    [Fact]
    public async Task FileOpenForWritingElsewhere_CanStillBeHashed()
    {
        var path = _temp.File("busy.log", "hello"u8.ToArray());
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var result = await new HashEngine().ComputeFileAsync(path, HashType.MD5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("5D41402ABC4B2A76B9719D911017C592", result.ToHex());
    }

    [Fact]
    public async Task CancellationDuringRead_Throws_AndReleasesTheFile()
    {
        var path = _temp.File("big.bin", RandomBytes(4 * 1024 * 1024));
        using var cts = new CancellationTokenSource();
        var engine = new HashEngine(new HashEngineOptions { BufferSize = 4096 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ComputeFileAsync(path, HashType.SHA256, _ => cts.Cancel(), cts.Token));

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task EmptyAlgorithmList_IsRejected()
    {
        var path = _temp.File("x.bin", [1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new HashEngine().ComputeFileAsync(path, Array.Empty<HashType>(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Directory_ThrowsUnauthorizedOrIo()
    {
        var exception = await Record.ExceptionAsync(() =>
            new HashEngine().ComputeFileAsync(_temp.Path, HashType.SHA256, cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(exception is UnauthorizedAccessException or IOException, exception?.GetType().FullName);
    }
}
