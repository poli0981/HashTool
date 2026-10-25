using System;
using System.IO;
using System.Threading.Tasks;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.Cli.Tests;

/// <summary>Error paths and their exit codes (0 ok, 1 failed or mismatched file, 2 usage error).</summary>
public sealed class CliErrorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter { NewLine = "\n" };
        using var error = new StringWriter { NewLine = "\n" };
        var exitCode = await HashToolCommand.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Sfv_NeedsCrc32()
    {
        var (exitCode, _, error) = await RunAsync("hash", _temp.File("a.txt", "x"), "-f", "sfv");

        Assert.Equal(2, exitCode);
        Assert.Contains("sfv files contain CRC32 only", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingInput_FailsButHashesTheRest()
    {
        var file = _temp.File("a.txt", "x");

        var (exitCode, output, error) = await RunAsync("hash", file, Path.Combine(_temp.Path, "missing.txt"));

        Assert.Equal(1, exitCode);
        Assert.Contains("a.txt", output, StringComparison.Ordinal);
        Assert.Contains("missing.txt: No such file or directory", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALockedInput_IsReported()
    {
        var file = _temp.File("locked.bin", "x");
        await using var lockHandle = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var (exitCode, _, error) = await RunAsync("hash", file);

        Assert.Equal(1, exitCode);
        Assert.Contains("locked.bin", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnwritableOutput_IsReported()
    {
        var file = _temp.File("a.txt", "x");
        var blocker = _temp.File("not-a-folder", "x");

        var (exitCode, _, error) = await RunAsync("hash", file, "-o", Path.Combine(blocker, "SHA256SUMS"));

        Assert.Equal(1, exitCode);
        Assert.Contains("SHA256SUMS", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_AFileWithoutChecksumLines_IsAUsageError()
    {
        var (exitCode, _, error) = await RunAsync("verify", _temp.File("notes.sha256", "just some notes\n"));

        Assert.Equal(2, exitCode);
        Assert.Contains("no properly formatted checksum lines found", error, StringComparison.Ordinal);
        Assert.Contains("1 line is improperly formatted", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_AHashOfTheWrongLength_IsNotValidForTheAlgorithm()
    {
        var file = _temp.File("a.txt", "x");

        var (exitCode, output, _) = await RunAsync("check", file, "-e", "abcd", "-a", "sha256");

        Assert.Equal(1, exitCode);
        Assert.Contains("FAILED (not a valid hash for the algorithm)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_AFolderListedAsFile_CannotBeRead()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "folder"));
        var sums = _temp.File("SUMS", $"{new string('0', 64)}  folder\n");

        var (exitCode, output, error) = await RunAsync("verify", sums);

        Assert.Equal(1, exitCode);
        Assert.Contains("folder: FAILED open or read", output, StringComparison.Ordinal);
        Assert.Contains("could not be read", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorsAreDescribedLikeCoreutils()
    {
        Assert.Equal("No such file or directory", HashToolCommand.Describe(new FileNotFoundException()));
        Assert.Equal("Permission denied", HashToolCommand.Describe(new UnauthorizedAccessException()));
        Assert.Equal("disk full", HashToolCommand.Describe(new IOException("disk full")));
    }
}
