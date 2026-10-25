using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

public sealed class CheckHashViewModelTests
{
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
    private const string HelloMd5 = "5d41402abc4b2a76b9719d911017c592";
    private const string HelloBlake3 = "ea8f163db38682925e4491c5e58d4bb3506ef8c14eb78a86e908c5624a67200f";

    private sealed class Page : IDisposable
    {
        public Page()
        {
            Settings = TestServices.Settings(Path.Combine(Temp.Path, ".settings"));
            Vm = new CheckHashViewModel(TestServices.Create(Dialogs, Storage, null, Settings));
        }

        public TempDirectory Temp { get; } = new();

        public SettingsService Settings { get; }

        public RecordingDialogs Dialogs { get; } = new();

        public ScriptedStorage Storage { get; } = new();

        public CheckHashViewModel Vm { get; }

        public FileItem Row(string name) => Vm.Items.Single(i => i.FileName == name);

        public void Dispose()
        {
            Settings.Dispose();
            Temp.Dispose();
        }
    }

    private static string F(string key, params object?[] args) => TestServices.Localization.Format(key, args);

    private static void Sparse(string path, long length)
    {
        using var stream = File.Create(path);
        stream.SetLength(length);
    }

    [AvaloniaFact]
    public async Task AChecksumFile_ListsEveryEntry_AndReportsWhatItCannotUse()
    {
        using var page = new Page();
        page.Temp.File("present.txt", "hello");
        var sums = page.Temp.File("SHA256SUMS",
            $"{HelloSha256}  present.txt\n{HelloSha256}  missing.txt\n{HelloSha256}  //server/share/x.txt\nnot a checksum line\n");

        await page.Vm.AddPathsAsync([sums]);

        Assert.Equal(ItemStatus.FromChecksumFile, page.Row("present.txt").Status);
        Assert.Equal(ItemStatus.NotFound, page.Row("missing.txt").Status);
        Assert.Equal(FileStatus.Failure, page.Row("missing.txt").State);
        Assert.Equal(2, page.Vm.Items.Count);
        var (message, icon) = page.Dialogs.Shown.Single();
        Assert.Contains(F("Msg_ChecksumFileInvalid", 1, "SHA256SUMS"), message, StringComparison.Ordinal);
        Assert.Contains(F("Msg_UnsafePath", "//server/share/x.txt"), message, StringComparison.Ordinal);
        Assert.Equal(DialogIcon.Warning, icon);
    }

    [AvaloniaFact]
    public async Task UnusableChecksumFiles_AreReported()
    {
        using var page = new Page();
        var empty = page.Temp.File("empty.sha256", "nothing useful here\n");
        var huge = Path.Combine(page.Temp.Path, "huge.md5");
        Sparse(huge, 17L * 1024 * 1024);

        await page.Vm.AddPathsAsync([empty, huge]);

        Assert.Empty(page.Vm.Items);
        var message = page.Dialogs.Shown.Single().Message;
        Assert.Contains(F("Msg_ChecksumFileEmpty", "empty.sha256"), message, StringComparison.Ordinal);
        Assert.Contains("huge.md5", message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Verify_FindsSidecars_AndReportsRowsItCannotCheck()
    {
        using var page = new Page();
        var withSidecar = page.Temp.File("image.iso", "hello");
        page.Temp.File("image.iso.sha256", HelloSha256);
        var unreadableSidecar = page.Temp.File("other.iso", "hello");
        Sparse(unreadableSidecar + ".sha256", 17L * 1024 * 1024);
        var noHash = page.Temp.File("plain.bin", "hello");
        var gone = page.Temp.File("gone.bin", "hello");
        await page.Vm.AddPathsAsync([withSidecar, unreadableSidecar, noHash, gone]);
        File.Delete(gone);

        await page.Vm.RunAllCommand.ExecuteAsync(null);

        Assert.Equal(ItemStatus.Valid, page.Row("image.iso").Status);
        Assert.Equal(withSidecar + ".sha256", page.Row("image.iso").SourceChecksumFile);
        Assert.Equal(ItemStatus.MissingHash, page.Row("other.iso").Status);
        Assert.Equal(ItemStatus.MissingHash, page.Row("plain.bin").Status);
        Assert.Equal(ItemStatus.NotFound, page.Row("gone.bin").Status);
        var (message, icon) = page.Dialogs.Shown.Last();
        Assert.StartsWith(F("Msg_CheckResult", 4, 1, 3, 0), message, StringComparison.Ordinal);
        Assert.Equal(DialogIcon.Warning, icon);
    }

    [AvaloniaFact]
    public async Task Verify_TriesTheAlgorithmsAHashCouldBelongTo()
    {
        using var page = new Page();
        foreach (var name in new[] { "mismatch.txt", "blake3.txt", "crc.txt", "short.txt", "odd.txt", "md5row.txt" })
            page.Temp.File(name, name == "crc.txt" ? "123456789" : "hello");
        await page.Vm.AddPathsAsync([page.Temp.Path]);
        page.Row("mismatch.txt").ExpectedHash = new string('0', 64);
        page.Row("blake3.txt").ExpectedHash = HelloBlake3;
        page.Row("crc.txt").Algorithm = HashType.CRC32;
        page.Row("crc.txt").IsAlgorithmKnown = true;
        page.Row("crc.txt").ExpectedHash = "2639F4CB";
        page.Row("short.txt").IsAlgorithmKnown = true;
        page.Row("short.txt").ExpectedHash = "abc";
        page.Row("odd.txt").ExpectedHash = "0123456789";
        page.Row("md5row.txt").Algorithm = HashType.MD5;
        page.Row("md5row.txt").ExpectedHash = HelloSha256;

        await page.Vm.RunAllCommand.ExecuteAsync(null);

        Assert.Equal(ItemStatus.Mismatch, page.Row("mismatch.txt").Status);
        Assert.False(page.Row("mismatch.txt").IsMatch);
        Assert.Equal(ItemStatus.Valid, page.Row("blake3.txt").Status);
        Assert.Equal(HashType.BLAKE3, page.Row("blake3.txt").Algorithm);
        Assert.Equal(ItemStatus.ValidLegacyCrc, page.Row("crc.txt").Status);
        Assert.Equal(ItemStatus.LengthMismatch, page.Row("short.txt").Status);
        Assert.Equal(ItemStatus.LengthMismatch, page.Row("odd.txt").Status);
        Assert.Equal(ItemStatus.Valid, page.Row("md5row.txt").Status);
        Assert.Equal(HashType.SHA256, page.Row("md5row.txt").Algorithm);
    }

    [AvaloniaFact]
    public async Task LoadHashFile_FillsTheRowFromAnyFile()
    {
        using var page = new Page();
        var data = page.Temp.File("data.bin", "hello");
        await page.Vm.AddPathsAsync([data]);
        var row = page.Row("data.bin");

        await page.Vm.LoadHashFileAsync(row, page.Temp.File("tagged.txt", $"MD5 (data.bin) = {HelloMd5}\n"));
        Assert.Equal(ItemStatus.HashLoaded, row.Status);
        Assert.Equal(HashType.MD5, row.Algorithm);
        Assert.True(row.IsAlgorithmKnown);
        Assert.Equal(HelloMd5, row.ExpectedHash, ignoreCase: true);

        await page.Vm.LoadHashFileAsync(row, page.Temp.File("others.txt", $"{HelloSha256}  a.bin\n{HelloSha256}  b.bin\n"));
        Assert.Equal(ItemStatus.NoHashInFile, row.Status);

        await page.Vm.LoadHashFileAsync(row, Path.Combine(page.Temp.Path, "nowhere.sha256"));
        Assert.Equal(ItemStatus.UnreadableHashFile, row.Status);

        row.IsProcessing = true;
        await page.Vm.LoadHashFileAsync(row, page.Temp.File("ignored.sha256", HelloSha256));
        Assert.Equal(ItemStatus.UnreadableHashFile, row.Status);
    }

    [AvaloniaFact]
    public async Task BrowseAndOpen_UseThePickers()
    {
        using var page = new Page();
        var data = page.Temp.File("data.bin", "hello");
        await page.Vm.AddPathsAsync([data]);
        var row = page.Row("data.bin");

        page.Storage.Files = [page.Temp.File("expected.txt", HelloSha256)];
        await page.Vm.BrowseHashFileCommand.ExecuteAsync(row);
        Assert.Equal(HelloSha256, row.ExpectedHash);

        page.Storage.Files = [page.Temp.File("a.txt", "1"), page.Temp.File("b.txt", "2")];
        await page.Vm.BrowseHashFileCommand.ExecuteAsync(row);
        await page.Vm.BrowseHashFileCommand.ExecuteAsync(null);
        Assert.Equal(HelloSha256, row.ExpectedHash);

        page.Temp.File("second.bin", "hello");
        page.Storage.Files = [page.Temp.File("list.sha256", $"{HelloSha256}  second.bin\n")];
        await page.Vm.OpenChecksumFileCommand.ExecuteAsync(null);
        Assert.Equal(HelloSha256, page.Row("second.bin").ExpectedHash);

        page.Storage.Files = [];
        await page.Vm.OpenChecksumFileCommand.ExecuteAsync(null);
        Assert.Equal(2, page.Vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task ChangingTheDefaultAlgorithm_UpdatesRowsWithAGuessedAlgorithm()
    {
        using var page = new Page();
        page.Temp.File("guessed.bin", "hello");
        page.Temp.File("known.bin", "hello");
        await page.Vm.AddPathsAsync([page.Temp.Path]);
        page.Row("known.bin").IsAlgorithmKnown = true;

        page.Vm.DefaultAlgorithm = HashType.SHA512;

        Assert.Equal(HashType.SHA512, page.Row("guessed.bin").Algorithm);
        Assert.Equal(HashType.SHA256, page.Row("known.bin").Algorithm);
        Assert.Equal(HashType.SHA512, page.Settings.Current.VerifyAlgorithm);
    }

    [AvaloniaFact]
    public async Task ClearingResults_AndFailedRows_FollowTheExpectedHash()
    {
        using var page = new Page();
        page.Temp.File("good.bin", "hello");
        page.Temp.File("bad.bin", "hello");
        page.Temp.File("none.bin", "hello");
        await page.Vm.AddPathsAsync([page.Temp.Path]);
        page.Row("good.bin").ExpectedHash = HelloSha256;
        page.Row("bad.bin").ExpectedHash = new string('1', 64);
        await page.Vm.RunAllCommand.ExecuteAsync(null);

        page.Vm.ClearResultsCommand.Execute(null);
        Assert.Equal(ItemStatus.Waiting, page.Row("good.bin").Status);
        Assert.Equal(ItemStatus.Ready, page.Row("none.bin").Status);

        await page.Vm.RunAllCommand.ExecuteAsync(null);
        page.Vm.ClearFailedCommand.Execute(null);
        Assert.Equal("good.bin", Assert.Single(page.Vm.Items).FileName);
    }

    [AvaloniaFact]
    public async Task ARowBeingHashed_IsNotMergedWithANewChecksumFile()
    {
        using var page = new Page();
        var data = page.Temp.File("data.bin", "hello");
        await page.Vm.AddPathsAsync([data]);
        page.Row("data.bin").IsProcessing = true;

        await page.Vm.AddPathsAsync([page.Temp.File("data.bin.sha256", $"{HelloSha256}  data.bin\n")]);

        Assert.Empty(page.Row("data.bin").ExpectedHash);
    }

    [AvaloniaFact]
    public async Task CancellingAVerification_CountsCancelledRows()
    {
        using var page = new Page();
        for (var i = 0; i < 200; i++) page.Temp.File($"f{i:D3}.bin", "hello");
        await page.Vm.AddPathsAsync([page.Temp.Path]);
        foreach (var item in page.Vm.Items) item.ExpectedHash = HelloSha256;

        var run = page.Vm.RunAllCommand.ExecuteAsync(null);
        page.Vm.CancelCommand.Execute(null);
        await run;

        Assert.Contains(page.Vm.Items, i => i.State == FileStatus.Cancelled);
        Assert.Equal(DialogIcon.Warning, page.Dialogs.Shown.Last().Icon);
    }
}
