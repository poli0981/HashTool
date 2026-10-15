using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HashTool.Core.Files;
using Xunit;

namespace HashTool.Core.Tests.Files;

public sealed class FileEnumeratorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ExpandsFoldersRecursively_AndKeepsFiles()
    {
        var a = _temp.File("root/a.txt", "a");
        var b = _temp.File("root/sub/deeper/b.txt", "bb");
        var loose = _temp.File("loose.bin", "ccc");

        var entries = FileEnumerator.Expand([Path.Combine(_temp.Path, "root"), loose], cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Equal(new[] { a, b, loose }.Order(), entries.Select(e => e.FullPath).Order());
        Assert.Equal(2, entries.Single(e => e.FullPath == b).Length);
    }

    [Fact]
    public void MissingPaths_AreReportedNotThrown()
    {
        var errors = new List<string>();

        var entries = FileEnumerator.Expand([Path.Combine(_temp.Path, "nope.bin")], onError: (p, _) => errors.Add(p), cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(entries);
        Assert.Single(errors);
    }

    [Fact]
    public void HiddenFiles_AreSkippedUnlessRequested()
    {
        var visible = _temp.File("dir/visible.txt", "v");
        var hidden = _temp.File("dir/hidden.txt", "h");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        var dir = Path.Combine(_temp.Path, "dir");

        if (OperatingSystem.IsWindows())
            Assert.Equal([visible], FileEnumerator.EnumerateDirectory(dir, cancellationToken: TestContext.Current.CancellationToken).Select(e => e.FullPath));
        Assert.Equal(2, FileEnumerator.EnumerateDirectory(dir, new FileEnumerationOptions { IncludeHidden = true }, cancellationToken: TestContext.Current.CancellationToken).Count());
    }

    [Fact]
    public void SymbolicLinkCycles_DoNotLoop()
    {
        var file = _temp.File("loop/file.txt", "x");
        var loopDir = Path.Combine(_temp.Path, "loop");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(loopDir, "back"), loopDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating symbolic links needs Developer Mode or admin rights on this machine.");
        }

        var entries = FileEnumerator.EnumerateDirectory(loopDir, cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Equal([file], entries.Select(e => e.FullPath));
    }
}
