using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>
/// A Velopack feed in a temporary folder, as vpk publishes it, and an installed Hash Tool 1.2.0 that updates from it.
/// Nothing reaches the network, and nothing ever applies an update.
/// </summary>
internal sealed class LocalFeed : IDisposable
{
    public const string Channel = "test";

    public LocalFeed(string? newVersion = "1.3.0", string? notes = "## Changes\n- **Faster** hashing", bool withPackage = true)
    {
        Directory.CreateDirectory(FeedDirectory);
        Directory.CreateDirectory(PackagesDirectory);
        var assets = new List<object>();
        if (newVersion is not null)
        {
            var fileName = $"HashTool-{newVersion}-full.nupkg";
            var package = Path.Combine(FeedDirectory, fileName);
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using var nuspec = new StreamWriter(zip.CreateEntry("HashTool.nuspec").Open(), Encoding.UTF8);
                nuspec.Write($"<?xml version=\"1.0\"?><package><metadata><id>HashTool</id><version>{newVersion}</version>" +
                             "<authors>tests</authors><description>tests</description></metadata></package>");
            }

            var bytes = File.ReadAllBytes(package);
            if (!withPackage) File.Delete(package);
            assets.Add(new Dictionary<string, object?>
            {
                ["PackageId"] = "HashTool",
                ["Version"] = newVersion,
                ["Type"] = "Full",
                ["FileName"] = fileName,
                ["SHA1"] = Convert.ToHexString(SHA1.HashData(bytes)),
                ["SHA256"] = Convert.ToHexString(SHA256.HashData(bytes)),
                ["Size"] = bytes.LongLength,
                ["NotesMarkdown"] = notes
            });
        }

        File.WriteAllText(Path.Combine(FeedDirectory, $"releases.{Channel}.json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["Assets"] = assets }));
    }

    public TempDirectory Temp { get; } = new();

    public string FeedDirectory => Path.Combine(Temp.Path, "feed");

    public string PackagesDirectory => Path.Combine(Temp.Path, "packages");

    public UpdateManager Manager(IUpdateSource? source = null) =>
        new(source ?? new SimpleFileSource(new DirectoryInfo(FeedDirectory)), new UpdateOptions { ExplicitChannel = Channel },
            new TestVelopackLocator("HashTool", "1.2.0", PackagesDirectory, null));

    public UpdateService Service(IUpdateSource? source = null, HttpMessageHandler? http = null) =>
        new(NullLogger<UpdateService>.Instance, Manager(source), new HttpClient(http ?? new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

    public void Dispose() => Temp.Dispose();
}

/// <summary>Answers HTTP requests from the test instead of the network.</summary>
internal sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsolutePath);
        return Task.FromResult(respond(request));
    }
}

/// <summary>
/// A release source that fails with <c>feedError</c>, never answers (no error), or with <see cref="Feed"/> lists that feed but
/// never finishes downloading.
/// </summary>
internal sealed class TroubledSource(Exception? feedError = null) : IUpdateSource
{
    public IUpdateSource? Feed { get; init; }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null) =>
        Feed?.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease)
        ?? (feedError is null ? new TaskCompletionSource<VelopackAssetFeed>().Task : Task.FromException<VelopackAssetFeed>(feedError));

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress,
        CancellationToken cancelToken = default) =>
        Task.Delay(Timeout.Infinite, cancelToken);
}

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task WithoutAnInstallation_UpdatesAreUnavailable()
    {
        var service = new UpdateService(NullLogger<UpdateService>.Instance, null, new HttpClient(new StubHttp(_ => throw new InvalidOperationException())));

        Assert.False(service.IsInstalled);
        Assert.False(service.IsPortable);
        Assert.False(service.IsUpdatePendingRestart);
        Assert.False(string.IsNullOrEmpty(service.CurrentVersion));
        Assert.Equal(UpdateCheckStatus.NotInstalled, (await service.CheckAsync(TestContext.Current.CancellationToken)).Status);

        using var feed = new LocalFeed();
        var update = (await feed.Service().CheckAsync(TestContext.Current.CancellationToken)).Update!;
        await service.DownloadAsync(update, null, TestContext.Current.CancellationToken);
        // Without an update manager these only log: nothing is applied and nothing exits.
        service.ApplyAndRestart(update);
        service.ApplyOnExit(update);
    }

    [Fact]
    public async Task AnInstalledApp_FindsTheNewerRelease_WithItsNotes()
    {
        using var feed = new LocalFeed();
        var service = feed.Service();

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(service.IsInstalled);
        Assert.Equal("1.2.0", service.CurrentVersion);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.3.0", result.Version);
        Assert.Contains("Faster", result.ReleaseNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutNewerRelease_TheAppIsUpToDate()
    {
        using var feed = new LocalFeed(newVersion: null);

        Assert.Equal(UpdateCheckStatus.UpToDate, (await feed.Service().CheckAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task NotesMissingFromTheFeed_AreReadFromTheGitHubRelease()
    {
        using var feed = new LocalFeed(notes: null);
        var http = new StubHttp(request => request.RequestUri!.AbsolutePath.EndsWith("/v1.3.0", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"body\":\"From GitHub\"}") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await feed.Service(http: http).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("From GitHub", result.ReleaseNotes);
        Assert.Equal(["/repos/poli0981/HashTool/releases/tags/1.3.0", "/repos/poli0981/HashTool/releases/tags/v1.3.0"], http.Requests);
    }

    [Fact]
    public async Task UnreachableReleaseNotes_LeaveTheNotesEmpty()
    {
        using var feed = new LocalFeed(notes: null);

        var result = await feed.Service(http: new StubHttp(_ => throw new HttpRequestException("offline"))).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.ReleaseNotes);
    }

    public static TheoryData<Exception, UpdateCheckStatus> Failures => new()
    {
        { new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden), UpdateCheckStatus.RateLimited },
        { new HttpRequestException("slow down", null, HttpStatusCode.TooManyRequests), UpdateCheckStatus.RateLimited },
        { new HttpRequestException("no route"), UpdateCheckStatus.NetworkError },
        { new TimeoutException("stalled"), UpdateCheckStatus.NetworkError },
        { new TaskCanceledException("client time-out"), UpdateCheckStatus.NetworkError },
        { new InvalidDataException("broken feed"), UpdateCheckStatus.Failed }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task CheckFailures_AreClassified(Exception error, UpdateCheckStatus expected)
    {
        using var feed = new LocalFeed();

        var result = await feed.Service(new TroubledSource(error)).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task ACancelledCheck_IsCancelled()
    {
        using var feed = new LocalFeed();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed.Service(new TroubledSource()).CheckAsync(cancelled.Token));
    }

    [Fact]
    public async Task Download_FetchesThePackageAndReportsProgress()
    {
        using var feed = new LocalFeed();
        var service = feed.Service();
        var update = (await service.CheckAsync(TestContext.Current.CancellationToken)).Update!;
        var progress = new List<int>();

        await service.DownloadAsync(update, p =>
        {
            lock (progress) progress.Add(p);
        }, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(feed.PackagesDirectory, "HashTool-1.3.0-full.nupkg")));
        lock (progress) Assert.Contains(100, progress);
    }
}

[Collection(SharedStateCollection.Name)]
public sealed class UpdateViewModelTests
{
    private sealed class Page : IDisposable
    {
        public Page(UpdateService updates)
        {
            Settings = TestServices.Settings(Path.Combine(Temp.Path, ".settings"));
            Vm = new UpdateViewModel(updates, Settings, TestServices.Localization, Launcher, TestServices.Loggers.CreateLogger<UpdateViewModel>());
        }

        public TempDirectory Temp { get; } = new();

        public SettingsService Settings { get; }

        public RecordingLauncher Launcher { get; } = new();

        public UpdateViewModel Vm { get; }

        public void Dispose()
        {
            Settings.Dispose();
            Temp.Dispose();
        }
    }

    private static string L(string key) => TestServices.Localization[key];

    private static UpdateService NotInstalled() =>
        new(NullLogger<UpdateService>.Instance, null, new HttpClient(new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

    [AvaloniaFact]
    public async Task AnUninstalledBuild_CannotCheck()
    {
        using var page = new Page(NotInstalled());

        Assert.Equal(UpdateState.NotInstalled, page.Vm.State);
        Assert.False(page.Vm.CanCheck);
        Assert.Equal(L("Msg_UpdatesUnavailable"), page.Vm.StatusText);

        await page.Vm.CheckCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.NotInstalled, page.Vm.State);
        Assert.NotNull(page.Settings.Current.LastUpdateCheck);
    }

    [AvaloniaFact]
    public async Task Check_ShowsTheNewVersionAndItsNotes()
    {
        using var feed = new LocalFeed();
        using var page = new Page(feed.Service());

        await page.Vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(UpdateState.Available, page.Vm.State);
        Assert.Equal("1.3.0", page.Vm.AvailableVersion);
        Assert.Equal("Changes\n• Faster hashing", page.Vm.ReleaseNotes);
        Assert.Equal(TestServices.Localization.Format("Msg_UpdateAvailableBanner", "1.3.0"), page.Vm.StatusText);
        Assert.True(page.Vm.CanInstall);
        Assert.False(page.Vm.IsChecking);

        await page.Vm.OpenReleasePageCommand.ExecuteAsync(null);
        Assert.Equal($"{UpdateService.RepositoryUrl}/releases/tag/1.3.0", page.Launcher.Urls.Single());
    }

    [AvaloniaFact]
    public async Task Check_WithoutUpdate_IsUpToDate()
    {
        using var feed = new LocalFeed(newVersion: null);
        using var page = new Page(feed.Service());

        await page.Vm.CheckCommand.ExecuteAsync(null);
        await page.Vm.OpenReleasePageCommand.ExecuteAsync(null);

        Assert.Equal(UpdateState.UpToDate, page.Vm.State);
        Assert.Equal(L("Status_Latest"), page.Vm.StatusText);
        Assert.Equal($"{UpdateService.RepositoryUrl}/releases", page.Launcher.Urls.Single());
    }

    public static TheoryData<Exception, string> CheckErrors => new()
    {
        { new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden), "Error_ApiLimit" },
        { new HttpRequestException("no route"), "Error_Network" },
        { new InvalidDataException("broken feed"), "broken feed" }
    };

    [AvaloniaTheory]
    [MemberData(nameof(CheckErrors))]
    public async Task CheckErrors_AreExplained(Exception error, string expected)
    {
        using var feed = new LocalFeed();
        using var page = new Page(feed.Service(new TroubledSource(error)));

        await page.Vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(UpdateState.Error, page.Vm.State);
        Assert.Equal(expected.Contains(' ', StringComparison.Ordinal) ? expected : L(expected), page.Vm.ErrorDetail);
        Assert.Equal(TestServices.Localization.Format("Status_CheckError", page.Vm.ErrorDetail), page.Vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AFailedDownload_IsReported_AndNothingIsInstalled()
    {
        using var feed = new LocalFeed(withPackage: false);
        using var page = new Page(feed.Service());
        await page.Vm.CheckCommand.ExecuteAsync(null);

        await page.Vm.InstallOnExitCommand.ExecuteAsync(null);

        Assert.Equal(UpdateState.Error, page.Vm.State);
        Assert.False(string.IsNullOrEmpty(page.Vm.ErrorDetail));
    }

    [AvaloniaFact]
    public async Task CancellingTheDownload_GoesBackToAvailable()
    {
        using var feed = new LocalFeed();
        using var page = new Page(feed.Service(new TroubledSource { Feed = new SimpleFileSource(new DirectoryInfo(feed.FeedDirectory)) }));
        await page.Vm.CheckCommand.ExecuteAsync(null);

        var install = page.Vm.InstallCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => page.Vm.IsDownloading);
        page.Vm.CancelDownloadCommand.Execute(null);
        await install;

        Assert.Equal(UpdateState.Available, page.Vm.State);
        page.Vm.CancelDownloadCommand.Execute(null);
    }

    [AvaloniaFact]
    public void StatusText_FollowsTheState()
    {
        using var page = new Page(NotInstalled());

        page.Vm.State = UpdateState.Idle;
        Assert.Equal(L("Lbl_Status_Ready"), page.Vm.StatusText);
        page.Vm.State = UpdateState.Checking;
        Assert.Equal(L("Status_Checking"), page.Vm.StatusText);
        Assert.True(page.Vm.IsChecking);
        page.Vm.State = UpdateState.Downloading;
        page.Vm.DownloadProgress = 42;
        Assert.Equal(TestServices.Localization.Format("Status_Downloading", 42), page.Vm.StatusText);
        Assert.True(page.Vm.IsDownloading);
        page.Vm.State = UpdateState.ReadyToInstall;
        Assert.Equal(L("Status_ReadyToInstall"), page.Vm.StatusText);
        Assert.True(page.Vm.CanInstall);
        Assert.False(string.IsNullOrEmpty(page.Vm.CurrentVersionText));
    }

    [AvaloniaFact]
    public void CheckOnStartup_IsTheSharedSetting()
    {
        using var page = new Page(NotInstalled());
        var changed = 0;
        page.Vm.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(UpdateViewModel.CheckOnStartup) ? 1 : 0;

        page.Vm.CheckOnStartup = false;
        page.Settings.Update(s => s.CheckForUpdatesOnStartup = true);

        Assert.True(page.Vm.CheckOnStartup);
        Assert.True(changed >= 2);
    }

    [AvaloniaFact]
    public async Task BackgroundCheck_RunsAtMostOnceADay()
    {
        using var feed = new LocalFeed();
        using var page = new Page(feed.Service());
        var found = 0;
        page.Vm.UpdateFound += (_, _) => found++;
        var ci = Environment.GetEnvironmentVariable("CI");
        try
        {
            Environment.SetEnvironmentVariable("CI", null);
            page.Vm.CheckOnStartup = false;
            await page.Vm.CheckInBackgroundIfDueAsync();
            Assert.Equal(UpdateState.Idle, page.Vm.State);

            page.Vm.CheckOnStartup = true;
            await page.Vm.CheckInBackgroundIfDueAsync();
            Assert.Equal(UpdateState.Available, page.Vm.State);
            Assert.Equal(1, found);

            // Checked a moment ago: not due again.
            page.Vm.State = UpdateState.Idle;
            await page.Vm.CheckInBackgroundIfDueAsync();
            Assert.Equal(UpdateState.Idle, page.Vm.State);

            Environment.SetEnvironmentVariable("CI", "true");
            page.Settings.Update(s => s.LastUpdateCheck = null);
            await page.Vm.CheckInBackgroundIfDueAsync();
            Assert.Equal(UpdateState.Idle, page.Vm.State);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", ci);
        }
    }

    [AvaloniaFact]
    public void ReleaseNotes_WithoutText_AreEmpty()
    {
        Assert.Null(UpdateViewModel.ToPlainText(null));
        Assert.Null(UpdateViewModel.ToPlainText("  "));
    }
}
