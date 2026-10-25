using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>A Generate page of its own: settings, dialogs, pickers and clipboard belong to the test.</summary>
internal sealed class CreatePage : IDisposable
{
    public CreatePage()
    {
        Settings = TestServices.Settings(System.IO.Path.Combine(Temp.Path, ".settings"));
        Vm = new CreateHashViewModel(TestServices.Create(Dialogs, Storage, Clipboard, Settings));
    }

    public TempDirectory Temp { get; } = new();

    public SettingsService Settings { get; }

    public RecordingDialogs Dialogs { get; } = new();

    public ScriptedStorage Storage { get; } = new();

    public RecordingClipboard Clipboard { get; } = new();

    public CreateHashViewModel Vm { get; }

    public string Path(string name) => System.IO.Path.Combine(Temp.Path, name);

    public async Task<string[]> HashAsync(params (string Name, string Content)[] files)
    {
        var paths = files.Select(f => Temp.File(f.Name, f.Content)).ToArray();
        await Vm.AddPathsAsync(paths);
        await Vm.RunAllCommand.ExecuteAsync(null);
        return paths;
    }

    public void Dispose()
    {
        Settings.Dispose();
        Temp.Dispose();
    }
}

public sealed class CreateHashViewModelTests
{
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
    private const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";

    [AvaloniaFact]
    public async Task ExportsOneChecksumFile_WithPathsRelativeToIt()
    {
        using var page = new CreatePage();
        await page.HashAsync(("sub/a.txt", "hello"), ("b.txt", "hello"));
        page.Storage.SavePath = page.Path("SHA256SUMS");

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Equal("SHA256SUMS.sha256", page.Storage.SuggestedName);
        Assert.Equal(ExportFormat.Gnu, page.Storage.OfferedTypes[0].Tag);
        Assert.DoesNotContain(page.Storage.OfferedTypes, t => (ExportFormat)t.Tag! == ExportFormat.Sfv);
        Assert.Equal([$"{HelloSha256}  sub/a.txt", $"{HelloSha256}  b.txt"], (await File.ReadAllLinesAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(DialogIcon.Success, page.Dialogs.Shown[^1].Icon);
    }

    [AvaloniaTheory]
    [InlineData(ExportFormat.Json, "{0}.json", "\"hash\": \"" + HelloSha256 + "\"")]
    [InlineData(ExportFormat.Csv, "{0}.csv", "b.txt,5,SHA256," + HelloSha256)]
    [InlineData(ExportFormat.Bsd, "{0}.txt", "SHA256 (b.txt) = " + HelloSha256)]
    public async Task ExportsTheFormatPickedInTheDialog(ExportFormat format, string name, string expected)
    {
        using var page = new CreatePage();
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = page.Path(string.Format(System.Globalization.CultureInfo.InvariantCulture, name, "out"));
        page.Storage.PickType = types => types.Single(t => (ExportFormat)t.Tag! == format);

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Contains(expected, (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData("out.json", "\"path\": \"b.txt\"")]
    [InlineData("out.csv", "path,size,algorithm,hash")]
    [InlineData("out.txt", HelloSha256 + "  b.txt")]
    [InlineData("out.sha256", HelloSha256 + "  b.txt")]
    public async Task WithoutAPickedType_TheExtensionDecides(string fileName, string expected)
    {
        using var page = new CreatePage();
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = page.Path(fileName);

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Contains(expected, (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheDefaultFormatFromSettings_IsOfferedFirst()
    {
        using var page = new CreatePage();
        page.Settings.Update(s => s.DefaultExportFormat = ExportFormat.Csv);
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = page.Path("export");

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Equal(ExportFormat.Csv, page.Storage.OfferedTypes[0].Tag);
        Assert.Equal("checksums.csv", page.Storage.SuggestedName);
        Assert.StartsWith("path,size,algorithm,hash", (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Sfv_IsOfferedForCrc32_AndWritesUpperCase()
    {
        using var page = new CreatePage();
        page.Vm.SelectedAlgorithm = HashType.CRC32;
        await page.HashAsync(("check.txt", "123456789"));
        page.Storage.SavePath = page.Path("files.sfv");

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Contains(page.Storage.OfferedTypes, t => (ExportFormat)t.Tag! == ExportFormat.Sfv);
        Assert.Equal(["; Generated by Hash Tool", "check.txt CBF43926"], (await File.ReadAllLinesAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)));
        Assert.Equal(HashType.CRC32, page.Settings.Current.CreateAlgorithm);
    }

    [AvaloniaFact]
    public async Task SeveralAlgorithms_AreWrittenAsBsd()
    {
        using var page = new CreatePage();
        await page.HashAsync(("a.txt", "hello"));
        page.Vm.SelectedAlgorithm = HashType.MD5;
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = page.Path("mixed.sha256");
        page.Storage.PickType = types => types.First(t => (ExportFormat)t.Tag! == ExportFormat.Gnu);

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Equal("checksums.txt", page.Storage.SuggestedName);
        var text = (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken));
        Assert.Contains($"SHA256 (a.txt) = {HelloSha256}", text, StringComparison.Ordinal);
        Assert.Contains($"MD5 (b.txt) = {HelloMd5}", text, StringComparison.Ordinal);

        await page.Vm.CopyAllCommand.ExecuteAsync(null);
        Assert.Contains($"MD5 (b.txt) = {HelloMd5.ToUpperInvariant()}", Assert.Single(page.Clipboard.Texts), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASfvTypeForAnotherAlgorithm_FallsBackToBsd()
    {
        using var page = new CreatePage();
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = page.Path("out.sfv");
        page.Storage.SaveType = new FileTypeChoice("SFV", "*.sfv", ExportFormat.Sfv);

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);

        Assert.Equal($"SHA256 (b.txt) = {HelloSha256}", (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)).Trim());
    }

    [AvaloniaFact]
    public async Task NothingHashed_NothingToExport()
    {
        using var page = new CreatePage();
        page.Storage.SavePath = page.Path("never.txt");

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);
        await page.Vm.CompressCommand.ExecuteAsync(null);
        await page.Vm.CopyAllCommand.ExecuteAsync(null);

        Assert.Equal(2, page.Dialogs.Shown.Count(d => d.Message == TestServices.Localization["Msg_NothingToExport"]));
        Assert.False(File.Exists(page.Storage.SavePath));
        Assert.Empty(page.Clipboard.Texts);
    }

    [AvaloniaFact]
    public async Task CancellingTheSaveDialog_WritesNothing()
    {
        using var page = new CreatePage();
        await page.HashAsync(("b.txt", "hello"));

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);
        await page.Vm.CompressCommand.ExecuteAsync(null);
        await page.Vm.SaveHashFileCommand.ExecuteAsync(page.Vm.Items[0]);

        Assert.Empty(page.Dialogs.Shown);
        Assert.Single(Directory.GetFiles(page.Temp.Path));
    }

    [AvaloniaFact]
    public async Task WriteErrors_AreShownWithTheirDetails()
    {
        using var page = new CreatePage();
        await page.HashAsync(("b.txt", "hello"));
        page.Storage.SavePath = Path.Combine(page.Temp.Path, "missing", "folder", "out.txt");

        await page.Vm.ExportChecksumFileCommand.ExecuteAsync(null);
        await page.Vm.CompressCommand.ExecuteAsync(null);
        await page.Vm.SaveHashFileCommand.ExecuteAsync(page.Vm.Items[0]);

        Assert.Equal(3, page.Dialogs.Shown.Count(d => d.Icon == DialogIcon.Error));
        Assert.All(page.Dialogs.ErrorDetails, d => Assert.Contains("DirectoryNotFoundException", d, StringComparison.Ordinal));
        Assert.NotEqual(ItemStatus.Saved, page.Vm.Items[0].Status);
    }

    [AvaloniaFact]
    public async Task SaveHashFile_WritesASidecarForOneRow()
    {
        using var page = new CreatePage();
        var file = (await page.HashAsync(("photo.jpg", "hello")))[0];
        page.Storage.SavePath = file + ".sha256";

        await page.Vm.SaveHashFileCommand.ExecuteAsync(page.Vm.Items[0]);
        await page.Vm.SaveHashFileCommand.ExecuteAsync(null);

        Assert.Equal("photo.jpg.sha256", page.Storage.SuggestedName);
        Assert.Equal($"{HelloSha256.ToUpperInvariant()}  photo.jpg", (await File.ReadAllTextAsync(page.Storage.SavePath, TestContext.Current.CancellationToken)).Trim());
        Assert.Equal(ItemStatus.Saved, page.Vm.Items[0].Status);
    }

    [AvaloniaFact]
    public async Task Compress_ZipsOneHashFilePerFileAndAManifest()
    {
        using var page = new CreatePage();
        await page.HashAsync(("one/a.txt", "hello"), ("two/a.txt", "hello"));
        page.Storage.SavePath = page.Path("hashes.zip");

        await page.Vm.CompressCommand.ExecuteAsync(null);

        await using var zip = await ZipFile.OpenReadAsync(page.Storage.SavePath, TestContext.Current.CancellationToken);
        Assert.Equal(["CHECKSUMS.txt", "one/a.txt.sha256", "two/a.txt.sha256"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var manifest = new StreamReader(await zip.GetEntry("CHECKSUMS.txt")!.OpenAsync(TestContext.Current.CancellationToken));
        Assert.Contains($"SHA256 (one/a.txt) = {HelloSha256}", await manifest.ReadToEndAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(DialogIcon.Success, page.Dialogs.Shown[^1].Icon);
    }

    [AvaloniaFact]
    public async Task Compress_NamesThatDifferOnlyInCase_GetUniqueEntries()
    {
        using var page = new CreatePage();
        page.Temp.File("dup.txt", "hello");
        if (File.Exists(page.Path("DUP.txt"))) return; // case-insensitive file system: two such files cannot exist

        await page.HashAsync(("dup.txt", "hello"), ("DUP.txt", "hello"));
        page.Storage.SavePath = page.Path("hashes.zip");

        await page.Vm.CompressCommand.ExecuteAsync(null);

        await using var zip = await ZipFile.OpenReadAsync(page.Storage.SavePath, TestContext.Current.CancellationToken);
        Assert.Equal(3, zip.Entries.Select(e => e.FullName.ToUpperInvariant()).Distinct().Count());
    }

    [AvaloniaFact]
    public async Task AFileThatDisappeared_FailsAsNotFound()
    {
        using var page = new CreatePage();
        var file = page.Temp.File("gone.bin", "x");
        await page.Vm.AddPathsAsync([file]);
        File.Delete(file);

        await page.Vm.RunAllCommand.ExecuteAsync(null);

        Assert.Equal(ItemStatus.NotFound, page.Vm.Items[0].Status);
        Assert.Equal(FileStatus.Failure, page.Vm.Items[0].State);
    }

    [Fact]
    public void CommonDirectory_IsTheDeepestSharedFolder()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        string P(params string[] parts) => Path.Combine([root, .. parts]);

        Assert.Equal("", CreateHashViewModel.CommonDirectory([]));
        Assert.Equal(P("data", "a"), CreateHashViewModel.CommonDirectory([P("data", "a")]));
        Assert.Equal(P("data"), CreateHashViewModel.CommonDirectory([P("data", "a"), P("data", "b", "c")]));
        Assert.Equal(P("data", "a"), CreateHashViewModel.CommonDirectory([P("data", "a"), P("data", "a", "deeper")]));
        // A shared prefix is not a shared folder.
        Assert.Equal(P("data"), CreateHashViewModel.CommonDirectory([P("data", "photos"), P("data", "photos-2024")]));
    }

    [Fact]
    public void RelativePath_KeepsFilesOutsideTheFolderAbsolute()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var folder = Path.Combine(root, "data");

        Assert.Equal("a/b.txt", CreateHashViewModel.RelativePath(folder, Path.Combine(folder, "a", "b.txt")));
        Assert.Equal(Path.Combine(root, "other", "c.txt"), CreateHashViewModel.RelativePath(folder, Path.Combine(root, "other", "c.txt")));
    }
}
