using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Infrastructure;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core;
using HashTool.Core.Configuration;
using HashTool.Core.Files;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>The shared list behaviour on its own: row sizes can be faked, and a failing preparation can be requested.</summary>
internal sealed class TestListViewModel(FileListServices services) : FileListViewModelBase(services)
{
    public Dictionary<string, long> FakeSizes { get; } = new(StringComparer.Ordinal);

    public bool FailPreparation { get; set; }

    protected override ActivationVerb ElevationVerb => ActivationVerb.Create;

    protected override Task<IReadOnlyList<FileItem>> CreateItemsAsync(IReadOnlyList<FileEntry> files, ImportReport report) =>
        Task.FromResult<IReadOnlyList<FileItem>>(files
            .Select(f => new FileItem(f.FullPath, FakeSizes.GetValueOrDefault(Path.GetFileName(f.FullPath), f.Length), HashType.CRC32)).ToList());

    protected override Task<IReadOnlyList<FileItem>> PrepareAsync(IReadOnlyList<FileItem> items) =>
        FailPreparation ? Task.FromException<IReadOnlyList<FileItem>>(new InvalidOperationException("preparation failed")) : base.PrepareAsync(items);

    protected override HashJob? CreateJob(FileItem item) => new(item.FilePath, item.Size, [item.Algorithm], item.Cancellation?.Token ?? default);

    protected override void ApplyResult(FileItem item, HashJobResult result)
    {
        if (result.Status == HashJobStatus.Succeeded)
        {
            item.State = FileStatus.Success;
            item.SetStatus(ItemStatus.Done);
            return;
        }

        ApplyFailure(item, result);
    }

    public static void Fail(FileItem item, HashJobResult result) => ApplyFailure(item, result);

    public static string Clock(TimeSpan time) => FormatClock(time);

    public FileItem? Find(string path) => FindItem(path);
}

public sealed class FileListTests
{
    private sealed class Page : IDisposable
    {
        public Page(Func<string, bool>? answer = null)
        {
            Settings = TestServices.Settings(Path.Combine(Temp.Path, ".settings"));
            Dialogs = new RecordingDialogs { Answer = answer ?? (_ => false) };
            Vm = new TestListViewModel(TestServices.Create(Dialogs, Storage, Clipboard, Settings));
        }

        public TempDirectory Temp { get; } = new();

        public SettingsService Settings { get; }

        public RecordingDialogs Dialogs { get; }

        public ScriptedStorage Storage { get; } = new();

        public RecordingClipboard Clipboard { get; } = new();

        public TestListViewModel Vm { get; }

        public string[] Files(int count, string prefix = "f", string content = "x") =>
            Enumerable.Range(0, count).Select(i => Temp.File($"{prefix}{i:D3}.bin", content)).ToArray();

        public void Dispose()
        {
            Settings.Dispose();
            Temp.Dispose();
        }
    }

    private static string L(string key) => TestServices.Localization[key];

    private static string F(string key, params object?[] args) => TestServices.Localization.Format(key, args);

    [AvaloniaFact]
    public async Task Import_ReportsDuplicatesSilently_AndEmptyFolders()
    {
        using var page = new Page();
        var file = page.Temp.File("a.bin", "x");
        var empty = Directory.CreateDirectory(Path.Combine(page.Temp.Path, "empty")).FullName;

        await page.Vm.AddPathsAsync([file]);
        await page.Vm.AddPathsAsync([file]);
        Assert.Empty(page.Dialogs.Shown);
        Assert.Single(page.Vm.Items);

        await page.Vm.AddPathsAsync([empty]);
        Assert.Equal(L("Msg_EmptyFolder"), Assert.Single(page.Dialogs.Shown).Message);
        Assert.Equal(page.Vm.Items[0], page.Vm.Find(file));
        Assert.True(page.Vm.HasItems);
    }

    [AvaloniaFact]
    public async Task Import_StopsAtTheFileLimit_ForFolders()
    {
        using var page = new Page();
        page.Settings.Update(s => s.MaxFileCount = 2);
        page.Files(5);

        await page.Vm.AddPathsAsync([page.Temp.Path]);

        Assert.Equal(2, page.Vm.Items.Count);
        Assert.Equal(F("Msg_FileLimit", 2, 3), page.Dialogs.Shown.Single().Message);
    }

    [AvaloniaFact]
    public async Task Import_SkipsFilesOverTheSizeLimit()
    {
        using var page = new Page();
        page.Settings.Update(s =>
        {
            s.IsFileSizeLimitEnabled = true;
            s.FileSizeLimitValue = 1;
            s.FileSizeLimitUnit = FileSizeUnit.KB;
        });
        var large = page.Files(12, "large", new string('x', 4096));
        var small = page.Temp.File("small.bin", "x");

        await page.Vm.AddPathsAsync([.. large, small]);

        Assert.Equal(small, Assert.Single(page.Vm.Items).FilePath);
        var (message, icon) = page.Dialogs.Shown.Single();
        Assert.Contains("large000.bin", message, StringComparison.Ordinal);
        Assert.EndsWith("…", message, StringComparison.Ordinal);
        Assert.Equal(DialogIcon.Information, icon);
    }

    [AvaloniaFact]
    public async Task Import_ReportsUnreadablePathsAsWarning()
    {
        using var page = new Page();

        await page.Vm.AddPathsAsync([Path.Combine(page.Temp.Path, "missing.bin")]);

        var (message, icon) = page.Dialogs.Shown.Single();
        Assert.Contains("missing.bin", message, StringComparison.Ordinal);
        Assert.Equal(DialogIcon.Warning, icon);
        Assert.Empty(page.Vm.Items);
    }

    [AvaloniaFact]
    public async Task Import_WhileBusy_AsksToTryLater()
    {
        using var page = new Page();
        page.Vm.IsBusy = true;

        await page.Vm.AddPathsAsync(page.Files(1));
        await page.Vm.AddPathsAsync([]);

        Assert.Equal(L("Msg_BusyTryLater"), page.Dialogs.Shown.Single().Message);
        Assert.False(page.Vm.CanModify);
        Assert.Empty(page.Vm.Items);
    }

    [AvaloniaFact]
    public async Task Pickers_AddWhatWasPicked_WithinTheFolderLimit()
    {
        using var page = new Page();
        page.Storage.Files = page.Files(2);

        await page.Vm.AddFilesCommand.ExecuteAsync(null);
        Assert.Equal(2, page.Vm.Items.Count);

        page.Storage.Folders = Enumerable.Range(0, 4).Select(i => Directory.CreateDirectory(Path.Combine(page.Temp.Path, $"d{i}")).FullName).ToList();
        await page.Vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(F("Msg_FolderLimit", 3), page.Dialogs.Shown.Single().Message);

        page.Storage.Folders = [Directory.CreateDirectory(Path.Combine(page.Temp.Path, "one")).FullName];
        page.Temp.File("one/inside.bin", "y");
        await page.Vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(3, page.Vm.Items.Count);

        page.Storage.Files = [];
        page.Storage.Folders = [];
        await page.Vm.AddFilesCommand.ExecuteAsync(null);
        await page.Vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(3, page.Vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task Filters_ByNameStatusAndSize()
    {
        using var page = new Page();
        page.Vm.FakeSizes["small.bin"] = 10;
        page.Vm.FakeSizes["medium.bin"] = 5L * ByteSize.OneMB;
        page.Vm.FakeSizes["large.bin"] = 200L * ByteSize.OneMB;
        page.Vm.FakeSizes["huge.bin"] = 2L * ByteSize.OneGB;
        foreach (var name in page.Vm.FakeSizes.Keys) page.Temp.File(name, "x");
        await page.Vm.AddPathsAsync([page.Temp.Path]);
        Assert.Equal(4, page.Vm.Items.Count);

        foreach (var (filter, expected) in new[]
                 {
                     (FileSizeFilter.Small, "small.bin"), (FileSizeFilter.Medium, "medium.bin"), (FileSizeFilter.Large, "large.bin"),
                     (FileSizeFilter.ExtraLarge, "huge.bin")
                 })
        {
            page.Vm.SelectedSizeFilter = filter;
            Assert.Equal(expected, Assert.Single(page.Vm.Items).FileName);
        }

        page.Vm.SelectedSizeFilter = FileSizeFilter.All;
        page.Vm.SelectedStatusFilter = FileStatus.Success;
        Assert.Empty(page.Vm.Items);
        Assert.Equal($"{F("Lbl_TotalFiles", 0)} / 4", page.Vm.TotalFilesText);
        page.Vm.SelectedStatusFilter = null;

        page.Vm.SearchText = " MEDIUM ";
        await Wait.UntilAsync(() => page.Vm.Items.Count == 1);
        Assert.Equal("medium.bin", page.Vm.Items[0].FileName);
        page.Vm.SearchText = "";
        await Wait.UntilAsync(() => page.Vm.Items.Count == 4);
        Assert.Equal(F("Lbl_TotalFiles", 4), page.Vm.TotalFilesText);
    }

    [AvaloniaFact]
    public async Task ClearList_AsksFirstForLongLists()
    {
        var answer = false;
        using var page = new Page(_ => answer);
        await page.Vm.AddPathsAsync(page.Files(21));

        await page.Vm.ClearListCommand.ExecuteAsync(null);
        Assert.Equal(21, page.Vm.Items.Count);
        Assert.Equal(F("Msg_ConfirmClear", 21), page.Dialogs.Confirmations.Single());

        answer = true;
        await page.Vm.ClearListCommand.ExecuteAsync(null);
        Assert.Empty(page.Vm.Items);
        Assert.False(page.Vm.HasItems);

        await page.Vm.ClearListCommand.ExecuteAsync(null);
        Assert.Single(page.Dialogs.Confirmations.Skip(1));
    }

    [AvaloniaFact]
    public async Task RowCommands_RemoveAndResetRows()
    {
        using var page = new Page();
        await page.Vm.AddPathsAsync(page.Files(4));
        await page.Vm.RunAllCommand.ExecuteAsync(null);
        Assert.All(page.Vm.Items, i => Assert.Equal(ItemStatus.Done, i.Status));

        page.Vm.ClearItemResultCommand.Execute(page.Vm.Items[0]);
        page.Vm.ClearItemResultCommand.Execute(null);
        Assert.Equal(ItemStatus.Ready, page.Vm.Items[0].Status);

        page.Vm.ClearResultsCommand.Execute(null);
        Assert.All(page.Vm.Items, i => Assert.Equal(FileStatus.Ready, i.State));

        var removed = page.Vm.Items[0];
        page.Vm.RemoveItemCommand.Execute(removed);
        page.Vm.RemoveItemCommand.Execute(null);
        Assert.True(removed.IsRemoved);
        Assert.Equal(3, page.Vm.Items.Count);

        page.Vm.SelectedItem = page.Vm.Items[0];
        page.Vm.RemoveSelectedCommand.Execute(null);
        Assert.Equal(2, page.Vm.Items.Count);

        page.Vm.Items[0].IsProcessing = true;
        page.Vm.RemoveItemCommand.Execute(page.Vm.Items[0]);
        page.Vm.ClearItemResultCommand.Execute(page.Vm.Items[0]);
        Assert.Equal(2, page.Vm.Items.Count);
    }

    [AvaloniaFact]
    public async Task ClearFailed_RemovesFailedAndCancelledRows()
    {
        using var page = new Page();
        var files = page.Files(3);
        await page.Vm.AddPathsAsync(files);
        File.Delete(files[0]);

        await page.Vm.RunAllCommand.ExecuteAsync(null);
        Assert.Equal(ItemStatus.NotFound, page.Vm.Find(files[0])!.Status);
        Assert.Equal(1, page.Vm.FailedCount);
        Assert.Equal(DialogIcon.Warning, page.Dialogs.Shown.Last().Icon);

        page.Vm.ClearFailedCommand.Execute(null);
        Assert.Equal(2, page.Vm.Items.Count);
        Assert.Null(page.Vm.Find(files[0]));
    }

    [AvaloniaFact]
    public async Task CancellingABatch_CancelsEveryRow()
    {
        using var page = new Page();
        await page.Vm.AddPathsAsync(page.Files(300));

        var run = page.Vm.RunAllCommand.ExecuteAsync(null);
        page.Vm.CancelCommand.Execute(null);
        await run;

        Assert.True(page.Vm.CancelledCount > 0);
        Assert.Equal(300, page.Vm.ProcessedCount);
        Assert.Equal(DialogIcon.Warning, page.Dialogs.Shown.Last().Icon);
        Assert.False(page.Vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task RunItem_HashesOneRow_OrStopsItWhileRunning()
    {
        using var page = new Page();
        await page.Vm.AddPathsAsync(page.Files(2));
        var row = page.Vm.Items[0];

        await page.Vm.RunItemCommand.ExecuteAsync(row);
        await page.Vm.RunItemCommand.ExecuteAsync(null);
        Assert.Equal(ItemStatus.Done, row.Status);
        Assert.Equal(ItemStatus.Ready, page.Vm.Items[1].Status);
        Assert.Empty(page.Dialogs.Shown); // one row: no summary

        using var cancellation = new System.Threading.CancellationTokenSource();
        row.Cancellation = cancellation;
        row.IsProcessing = true;
        await page.Vm.RunItemCommand.ExecuteAsync(row);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [AvaloniaFact]
    public async Task ALargeFile_ShowsProgressAndSpeed()
    {
        using var page = new Page();
        page.Settings.Update(s => s.ShowReadWriteSpeed = true);
        var big = Path.Combine(page.Temp.Path, "big.bin");
        await using (var stream = File.Create(big))
        {
            var block = new byte[1024 * 1024];
            Random.Shared.NextBytes(block);
            for (var i = 0; i < 80; i++) await stream.WriteAsync(block);
        }

        await page.Vm.AddPathsAsync([big, page.Temp.File("small.bin", "x")]);
        await page.Vm.RunAllCommand.ExecuteAsync(null);

        Assert.All(page.Vm.Items, i => Assert.Equal(FileStatus.Success, i.State));
        Assert.Equal(1, page.Vm.ProgressValue);
        Assert.False(string.IsNullOrEmpty(page.Vm.ProgressText));
        Assert.False(string.IsNullOrEmpty(page.Vm.DetailText));
    }

    [AvaloniaFact]
    public async Task AFailingPreparation_IsShownAsUnexpectedError()
    {
        using var page = new Page();
        await page.Vm.AddPathsAsync(page.Files(2));
        page.Vm.FailPreparation = true;

        await page.Vm.RunAllCommand.ExecuteAsync(null);

        Assert.Contains(page.Dialogs.Shown, d => d.Icon == DialogIcon.Error && d.Message.Contains("preparation failed", StringComparison.Ordinal));
        Assert.False(page.Vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task CopyText_PutsTheTextOnTheClipboard()
    {
        using var page = new Page();

        await page.Vm.CopyTextCommand.ExecuteAsync("ABC");
        await page.Vm.CopyTextCommand.ExecuteAsync("");
        await page.Vm.CopyTextCommand.ExecuteAsync(null);

        Assert.Equal(["ABC"], page.Clipboard.Texts);
    }

    [AvaloniaFact]
    public void SearchFocus_IsForwardedToTheView()
    {
        using var page = new Page();
        var raised = 0;
        page.Vm.FocusSearchRequested += (_, _) => raised++;

        page.Vm.RequestSearchFocus();

        Assert.Equal(1, raised);
        Assert.Contains(HashType.SHA256, page.Vm.Algorithms);
        Assert.Contains(null, page.Vm.StatusFilterOptions);
        Assert.Contains(FileSizeFilter.ExtraLarge, page.Vm.SizeFilterOptions);
    }

    [Theory]
    [InlineData(HashJobStatus.Cancelled, null, ItemStatus.Cancelled, FileStatus.Cancelled)]
    [InlineData(HashJobStatus.TimedOut, null, ItemStatus.TimedOut, FileStatus.Cancelled)]
    [InlineData(HashJobStatus.Failed, typeof(FileNotFoundException), ItemStatus.NotFound, FileStatus.Failure)]
    [InlineData(HashJobStatus.Failed, typeof(UnauthorizedAccessException), ItemStatus.AccessDenied, FileStatus.Failure)]
    [InlineData(HashJobStatus.Failed, typeof(InvalidOperationException), ItemStatus.Failed, FileStatus.Failure)]
    [InlineData(HashJobStatus.Failed, null, ItemStatus.Failed, FileStatus.Failure)]
    public void Failures_MapToRowStatuses(HashJobStatus status, Type? error, ItemStatus expected, FileStatus state)
    {
        var item = new FileItem(Path.Combine(Path.GetTempPath(), "x.bin"), 1, HashType.SHA256) { IsMatch = true };
        var exception = error is null ? null : (Exception)Activator.CreateInstance(error, "message")!;

        TestListViewModel.Fail(item, new HashJobResult(status, null, exception, TimeSpan.Zero));

        Assert.Equal(expected, item.Status);
        Assert.Equal(state, item.State);
        Assert.Equal(state == FileStatus.Cancelled ? null : false, item.IsMatch);
    }

    [Fact]
    public void Clock_ShowsHoursMinutesAndSeconds()
    {
        Assert.Equal("0:00:05", TestListViewModel.Clock(TimeSpan.FromSeconds(5)));
        Assert.Equal("26:03:04", TestListViewModel.Clock(new TimeSpan(1, 2, 3, 4)));
        Assert.Equal("15 ms", FileItem.FormatDuration(TimeSpan.FromMilliseconds(15)));
    }
}
