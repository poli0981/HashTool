using System;
using System.IO;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.ViewModels;
using HashTool.App.Views.Controls;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

public sealed class HighlightTextBlockTests
{
    [Fact]
    public void Split_EmitsTheTailOnce()
    {
        Assert.Equal([("rep", true), ("ort.pdf", false)], HighlightTextBlock.Split("report.pdf", "rep"));
    }

    [Fact]
    public void Split_IsCaseInsensitiveAndFindsEveryMatch()
    {
        Assert.Equal([("a", false), ("X", true), ("a", false), ("x", true)], HighlightTextBlock.Split("aXax", "x"));
    }

    [Fact]
    public void Split_WithoutMatch_ReturnsTheText()
    {
        Assert.Equal([("report.pdf", false)], HighlightTextBlock.Split("report.pdf", "zip"));
        Assert.Equal([("abc", false)], HighlightTextBlock.Split("abc", ""));
    }
}

public sealed class AppCommandLineTests
{
    [Fact]
    public void Parse_VerbAndPaths()
    {
        var parsed = AppCommandLine.Parse(["--verify", "a.sha256", "sub/b.md5"], workingDirectory: Path.GetTempPath());

        Assert.Equal(ActivationVerb.Verify, parsed.Verb);
        Assert.Equal([Path.Combine(Path.GetTempPath(), "a.sha256"), Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sub/b.md5"))], parsed.Paths);
        Assert.False(parsed.SmokeTest);
    }

    [Fact]
    public void Parse_SmokeTestOptions()
    {
        var parsed = AppCommandLine.Parse(["--smoke-test", "--smoke-out", "r.json", "--expect-version", "1.2.0", "--wait-pid", "42", "--unknown"]);

        Assert.True(parsed.SmokeTest);
        Assert.Equal("r.json", parsed.SmokeOutput);
        Assert.Equal("1.2.0", parsed.ExpectedVersion);
        Assert.Equal(42, parsed.WaitPid);
        Assert.Empty(parsed.Paths);
        Assert.Null(parsed.ToRequest());
    }

    [Fact]
    public void FilesFrom_DeletesOnlyTheAppsOwnTemporaryList()
    {
        using var temp = new TempDirectory();
        var ownList = Path.Combine(Path.GetTempPath(), $"hashtool-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(ownList, [Path.Combine(temp.Path, "a.bin")]);
        var userFile = temp.File("important.txt", Path.Combine(temp.Path, "b.bin"));

        var fromOwn = AppCommandLine.Parse(["--create", "--files-from", ownList]);
        var fromUser = AppCommandLine.Parse(["--files-from", userFile]);

        Assert.Equal([Path.Combine(temp.Path, "a.bin")], fromOwn.Paths);
        Assert.False(File.Exists(ownList));
        Assert.Equal([Path.Combine(temp.Path, "b.bin")], fromUser.Paths);
        Assert.True(File.Exists(userFile));
    }
}

public sealed class TextHelperTests
{
    [Fact]
    public void ReleaseNotes_MarkdownBecomesPlainText()
    {
        var text = UpdateViewModel.ToPlainText("## Added\n\n- **CLI** `hashtool`\n- See [docs](https://example.com)\n\n\n\nEnd");

        Assert.Equal("Added\n\n• CLI hashtool\n• See docs\n\nEnd", text);
    }

    [Fact]
    public void PathSanitizer_MasksUserFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sanitized = PathSanitizer.Shared.Sanitize($"Could not read {Path.Combine(home, "Documents", "secret.txt")}");

        Assert.DoesNotContain(home, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("secret.txt", sanitized);
    }

    [Theory]
    [InlineData("C:/data", "C:/data/a/b.txt", "a/b.txt")]
    [InlineData("C:/data", "C:/data/b.txt", "b.txt")]
    public void RelativePath_UsesForwardSlashes(string baseDirectory, string file, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, CreateHashViewModel.RelativePath(baseDirectory, file));
    }

    [Fact]
    public void LogEntry_UsesInvariantTimestamps()
    {
        var entry = new LogEntry(new DateTimeOffset(2026, 10, 15, 8, 30, 5, 123, TimeSpan.Zero), Microsoft.Extensions.Logging.LogLevel.Warning, "Test", "message");

        Assert.Equal("08:30:05.123 [WARN] Test: message", entry.ToString());
    }
}
