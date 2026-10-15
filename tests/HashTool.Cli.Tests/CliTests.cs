using System;
using System.IO;
using System.Threading.Tasks;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.Cli.Tests;

public sealed class CliTests : IDisposable
{
    private const string Sha256Of123456789 = "15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225";
    private const string Sha256OfHello = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The CLI writes Windows paths with '/' separators.</summary>
    private static string Shown(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter { NewLine = "\n" };
        using var error = new StringWriter { NewLine = "\n" };
        var exitCode = await HashToolCommand.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Hash_SingleFile_PrintsGnuLine()
    {
        var file = _temp.File("check.txt", "123456789");

        var (exitCode, output, error) = await RunAsync("hash", file);

        Assert.Equal(0, exitCode);
        Assert.Equal($"{Sha256Of123456789}  {Shown(file)}\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Hash_SeveralAlgorithms_UsesTaggedFormat()
    {
        var file = _temp.File("check.txt", "123456789");

        var (exitCode, output, _) = await RunAsync("hash", file, "-a", "sha256,crc32", "--uppercase");

        Assert.Equal(0, exitCode);
        Assert.Contains($"SHA256 ({Shown(file)}) = {Sha256Of123456789.ToUpperInvariant()}", output);
        Assert.Contains($"CRC32 ({Shown(file)}) = CBF43926", output);
    }

    [Fact]
    public async Task Hash_GnuWithSeveralAlgorithms_IsUsageError()
    {
        var file = _temp.File("check.txt", "123456789");

        var (exitCode, _, error) = await RunAsync("hash", file, "-a", "sha256", "-a", "md5", "-f", "gnu");

        Assert.Equal(2, exitCode);
        Assert.Contains("single algorithm", error);
    }

    [Fact]
    public async Task Hash_UnknownAlgorithm_IsUsageError()
    {
        var (exitCode, output, error) = await RunAsync("hash", "x", "-a", "sha3");

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains("Unknown algorithm 'sha3'", error);
    }

    [Fact]
    public async Task Hash_Directory_RequiresRecursive()
    {
        _temp.File("dir/a.txt", "hello");

        var (exitCode, _, error) = await RunAsync("hash", Path.Combine(_temp.Path, "dir"));

        Assert.Equal(1, exitCode);
        Assert.Contains("is a directory", error);
    }

    [Fact]
    public async Task Hash_ToOutputFile_WritesRelativePathsThatVerify()
    {
        _temp.File("data/check.txt", "123456789");
        _temp.File("data/sub/hello.txt", "hello");
        var manifest = Path.Combine(_temp.Path, "data", "SHA256SUMS");

        var hash = await RunAsync("hash", "-r", Path.Combine(_temp.Path, "data"), "-o", manifest);
        var content = await File.ReadAllTextAsync(manifest, TestContext.Current.CancellationToken);
        var verify = await RunAsync("verify", manifest);

        Assert.Equal(0, hash.ExitCode);
        Assert.Contains($"{Sha256Of123456789}  check.txt\n", content);
        Assert.Contains($"{Sha256OfHello}  sub/hello.txt\n", content);
        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("check.txt: OK", verify.Output);
        Assert.Contains("sub/hello.txt: OK", verify.Output);
    }

    [Fact]
    public async Task Verify_ReportsMismatchAndMissingFiles()
    {
        _temp.File("a.txt", "changed");
        var manifest = _temp.File("SHA256SUMS", $"{Sha256OfHello}  a.txt\n{Sha256OfHello}  missing.txt\nnot a checksum line\n");

        var (exitCode, output, error) = await RunAsync("verify", manifest);

        Assert.Equal(1, exitCode);
        Assert.Contains("a.txt: FAILED\n", output);
        Assert.Contains("missing.txt: FAILED open or read", output);
        Assert.Contains("1 line is improperly formatted", error);
        Assert.Contains("1 listed file could not be read", error);
        Assert.Contains("1 computed checksum did NOT match", error);
    }

    [Fact]
    public async Task Verify_IgnoreMissing_And_Quiet()
    {
        _temp.File("a.txt", "hello");
        var manifest = _temp.File("SHA256SUMS", $"{Sha256OfHello}  a.txt\n{Sha256OfHello}  missing.txt\n");

        var (exitCode, output, _) = await RunAsync("verify", manifest, "--ignore-missing", "--quiet");

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
    }

    [Fact]
    public async Task Verify_SidecarWithBareHash()
    {
        _temp.File("image.iso", "hello");
        var sidecar = _temp.File("image.iso.sha256", Sha256OfHello.ToUpperInvariant() + "\n");

        var (exitCode, output, _) = await RunAsync("verify", sidecar);

        Assert.Equal(0, exitCode);
        Assert.Equal("image.iso: OK\n", output);
    }

    [Fact]
    public async Task Verify_RefusesNetworkPaths()
    {
        var manifest = _temp.File("SHA256SUMS", $@"{Sha256OfHello}  \\server\share\payload.bin" + "\n");

        var (exitCode, _, error) = await RunAsync("verify", manifest);

        Assert.Equal(1, exitCode);
        Assert.Contains("refusing to open network or device path", error);
    }

    [Fact]
    public async Task Verify_UnreadableChecksumFile_IsExitCode2()
    {
        var (exitCode, _, error) = await RunAsync("verify", Path.Combine(_temp.Path, "nope.sha256"));

        Assert.Equal(2, exitCode);
        Assert.Contains("nope.sha256", error);
    }

    [Theory]
    [InlineData("CBF43926", 0, "check.txt: OK\n")]
    [InlineData("2639F4CB", 0, "check.txt: OK (CRC32 written in reversed byte order)\n")]
    [InlineData("00000000", 1, "check.txt: FAILED\n")]
    public async Task Check_ComparesAgainstExpectedCrc32(string expected, int exitCode, string message)
    {
        var file = _temp.File("check.txt", "123456789");

        var result = await RunAsync("check", file, "--expected", expected, "-a", "crc32");

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(file + message[message.IndexOf(':')..], result.Output);
    }

    [Fact]
    public async Task Check_GuessesAlgorithmFromLength()
    {
        var file = _temp.File("check.txt", "123456789");

        var (exitCode, _, _) = await RunAsync("check", file, "-e", Sha256Of123456789.ToUpperInvariant());

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Check_RejectsNonHex()
    {
        var (exitCode, _, error) = await RunAsync("check", "whatever", "-e", "xyz");

        Assert.Equal(2, exitCode);
        Assert.Contains("not a hexadecimal hash", error);
    }

    [Fact]
    public async Task Algorithms_ListsEverySupportedAlgorithm()
    {
        var (exitCode, output, _) = await RunAsync("algorithms");

        Assert.Equal(0, exitCode);
        foreach (var name in new[] { "md5", "sha1", "sha256", "sha384", "sha512", "blake3", "xxhash32", "xxhash64", "xxhash3", "xxhash128", "crc32" })
            Assert.Contains(name, output);
    }
}
