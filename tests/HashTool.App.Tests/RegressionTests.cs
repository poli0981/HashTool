using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Infrastructure;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>Records dialogs instead of showing them, so batch summaries can be checked.</summary>
internal sealed class RecordingDialogs : IDialogService
{
    public List<(string Message, DialogIcon Icon)> Shown { get; } = [];

    public Task ShowAsync(string title, string message, DialogIcon icon = DialogIcon.Information)
    {
        Shown.Add((message, icon));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, DialogIcon icon = DialogIcon.Question, string? yesText = null,
        string? noText = null) => Task.FromResult(false);

    public Task ShowErrorAsync(string title, string message, string? details = null)
    {
        Shown.Add((message, DialogIcon.Error));
        return Task.CompletedTask;
    }
}

internal sealed class NoStorage : IStorageService
{
    public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, IReadOnlyList<FileTypeChoice>? types = null) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<(string Path, FileTypeChoice? Type)?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<FileTypeChoice> types) =>
        Task.FromResult<(string Path, FileTypeChoice? Type)?>(null);
}

internal sealed class NoClipboard : IClipboardService
{
    public Task SetTextAsync(string text) => Task.CompletedTask;
}

public sealed class BatchRegressionTests
{
    private static FileListServices Services(RecordingDialogs dialogs) => new(
        AppHost.Get<LocalizationService>(), AppHost.Get<SettingsService>(), dialogs, new NoStorage(), new NoClipboard(), new HashEngine(),
        AppHost.Get<ILoggerFactory>());

    [AvaloniaFact]
    public async Task LargeBatch_FinishesEveryRow()
    {
        using var temp = new TempDirectory();
        var files = Enumerable.Range(0, 2500).Select(i => temp.File($"f{i:D4}.bin", $"content {i}")).ToList();
        var dialogs = new RecordingDialogs();
        var vm = new CreateHashViewModel(Services(dialogs)) { SelectedAlgorithm = HashType.CRC32 };

        await vm.AddPathsAsync(files);
        await vm.RunAllCommand.ExecuteAsync(null);

        Assert.Equal(2500, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.Equal(FileStatus.Success, i.State));
        Assert.DoesNotContain(vm.Items, i => i.IsProcessing);
        Assert.Equal(2500, vm.SucceededCount);
        Assert.Equal(DialogIcon.Success, Assert.Single(dialogs.Shown).Icon);
    }

    [AvaloniaFact]
    public async Task Verify_SummaryCountsMissingFilesAsFailures()
    {
        using var temp = new TempDirectory();
        temp.File("present.txt", "hello");
        var sums = temp.File("SHA256SUMS",
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824  present.txt\n" +
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824  missing.txt\n");
        var dialogs = new RecordingDialogs();
        var vm = new CheckHashViewModel(Services(dialogs));

        await vm.AddPathsAsync([sums]);
        await vm.RunAllCommand.ExecuteAsync(null);

        Assert.Equal(DialogIcon.Warning, dialogs.Shown.Last().Icon);
        Assert.Single(vm.Items, i => i.IsMatch == true);
        Assert.Single(vm.Items, i => i.Status == ItemStatus.NotFound);
    }

    [AvaloniaFact]
    public async Task Verify_ChecksumFileForExistingRow_IsNotReportedAsEmpty()
    {
        using var temp = new TempDirectory();
        var file = temp.File("present.txt", "hello");
        var sums = temp.File("SHA256SUMS", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824  present.txt\n");
        var dialogs = new RecordingDialogs();
        var vm = new CheckHashViewModel(Services(dialogs));

        await vm.AddPathsAsync([file]);
        await vm.AddPathsAsync([sums]);

        Assert.Empty(dialogs.Shown);
        Assert.False(string.IsNullOrEmpty(Assert.Single(vm.Items).ExpectedHash));
    }
}

public sealed class FileItemTests
{
    [Fact]
    public void ChangingTheAlgorithmClearsAResultOfAnotherAlgorithm()
    {
        var item = new FileItem(Path.Combine(Path.GetTempPath(), "x.bin"), 1, HashType.SHA256);
        item.SetResult(HashType.SHA256, "AB");

        item.Algorithm = HashType.MD5;

        Assert.Empty(item.ResultHash);
        Assert.Null(item.ResultAlgorithm);
        Assert.Equal(ItemStatus.Ready, item.Status);
    }

    [Fact]
    public void KeepingTheAlgorithmKeepsTheResult()
    {
        var item = new FileItem(Path.Combine(Path.GetTempPath(), "x.bin"), 1, HashType.SHA256);
        item.SetResult(HashType.SHA256, "AB");

        item.Algorithm = HashType.SHA256;

        Assert.Equal("AB", item.ResultHash);
    }
}

public sealed class ActivationServiceTests
{
    [AvaloniaFact]
    public void RequestsArrivingTogether_AreDeliveredOnceAndInOrder()
    {
        var service = new ActivationService(AppHost.Get<ILoggerFactory>().CreateLogger<ActivationService>());
        var delivered = new List<ActivationRequest>();
        // Explorer starts one process per selected file; they all arrive before the window is ready.
        service.Enqueue(new ActivationRequest(ActivationVerb.Create, ["a"]));
        service.Enqueue(new ActivationRequest(ActivationVerb.Create, ["b"]));
        service.Enqueue(new ActivationRequest(ActivationVerb.Verify, ["c.sha256"]));
        service.Enqueue(new ActivationRequest(ActivationVerb.Auto, ["d"]));
        service.Enqueue(new ActivationRequest(ActivationVerb.Auto, ["e"]));

        service.Attach(request =>
        {
            delivered.Add(request);
            return Task.CompletedTask;
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([ActivationVerb.Create, ActivationVerb.Verify, ActivationVerb.Auto, ActivationVerb.Auto], delivered.Select(r => r.Verb));
        Assert.Collection(delivered,
            r => Assert.Equal(["a", "b"], r.Paths),
            r => Assert.Equal(["c.sha256"], r.Paths),
            r => Assert.Equal(["d"], r.Paths),
            r => Assert.Equal(["e"], r.Paths));
    }
}

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task ForwardedRequestsReachTheRunningInstance()
    {
        var name = $"HashTool-test-{Guid.NewGuid():N}"[..32];
        var received = new ConcurrentQueue<ActivationRequest>();
        using var primary = new SingleInstance(name);
        primary.StartServer(received.Enqueue);
        using var secondary = new SingleInstance(name);

        Assert.True(secondary.TryForward(new ActivationRequest(ActivationVerb.Verify, ["a.sha256", "b.md5"]), TimeSpan.FromSeconds(5)));
        Assert.True(secondary.TryForward(new ActivationRequest(ActivationVerb.Create, ["c.iso"]), TimeSpan.FromSeconds(5)));

        for (var i = 0; i < 100 && received.Count < 2; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        var requests = received.ToList();
        Assert.Equal(2, requests.Count);
        Assert.Equal(ActivationVerb.Verify, requests[0].Verb);
        Assert.Equal(["a.sha256", "b.md5"], requests[0].Paths);
        Assert.Equal(["c.iso"], requests[1].Paths);
    }

    [Fact]
    public void DriveRootFromExplorer_KeepsItsBackslash()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Windows turns "C:\" into C:" because \" is an escaped quote.
        var parsed = AppCommandLine.Parse(["--create", "C:\""]);

        Assert.Equal([@"C:\"], parsed.Paths);
    }
}
