using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.App.ViewModels;
using HashTool.Core.Configuration;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>A main window view model over pages of its own, so nothing leaks into the shared singletons.</summary>
internal sealed class MainPage : IDisposable
{
    public MainPage(bool corruptSettings = false)
    {
        var settingsFolder = Path.Combine(Temp.Path, ".settings");
        if (corruptSettings) Temp.File(".settings/config.json", "{ this is not json");
        Settings = TestServices.Settings(settingsFolder);
        var services = TestServices.Create(Dialogs, Storage, Clipboard, Settings);
        Create = new CreateHashViewModel(services);
        Check = new CheckHashViewModel(services);
        Theme = new ThemeService();
        var fonts = new FontService(TestServices.Loggers.CreateLogger<FontService>());
        var notInstalled = new UpdateService(TestServices.Loggers.CreateLogger<UpdateService>(), null, new System.Net.Http.HttpClient());
        SettingsPage = new SettingsViewModel(Settings, TestServices.Localization, fonts, Theme, Dialogs, Clipboard, Launcher, notInstalled,
            new LogLevelSwitch(), TestServices.Loggers.CreateLogger<SettingsViewModel>());
        Update = new UpdateViewModel(notInstalled, Settings, TestServices.Localization, Launcher, TestServices.Loggers.CreateLogger<UpdateViewModel>());
        Developer = new DeveloperViewModel(new LogSink(PathSanitizer.Shared), TestServices.Localization, Clipboard, Storage, Launcher);
        Activation = new ActivationService(TestServices.Loggers.CreateLogger<ActivationService>());
        Vm = new MainWindowViewModel(Create, Check, SettingsPage, Update, new AboutViewModel(notInstalled, TestServices.Localization, Launcher),
            Developer, Settings, TestServices.Localization, fonts, Theme, Activation);
    }

    public TempDirectory Temp { get; } = new();

    public SettingsService Settings { get; }

    public RecordingDialogs Dialogs { get; } = new();

    public ScriptedStorage Storage { get; } = new();

    public RecordingClipboard Clipboard { get; } = new();

    public RecordingLauncher Launcher { get; } = new();

    public CreateHashViewModel Create { get; }

    public CheckHashViewModel Check { get; }

    public ThemeService Theme { get; }

    public SettingsViewModel SettingsPage { get; }

    public UpdateViewModel Update { get; }

    public DeveloperViewModel Developer { get; }

    public ActivationService Activation { get; }

    public MainWindowViewModel Vm { get; }

    public void Dispose()
    {
        Settings.Dispose();
        Temp.Dispose();
    }
}

public sealed class MainWindowViewModelTests
{
    [AvaloniaFact]
    public void Navigation_SwitchesPages_ButNotAwayFromABusyList()
    {
        using var page = new MainPage();

        foreach (var (name, expected) in new (string, object)[]
                 {
                     ("Check", page.Check), ("Settings", page.SettingsPage), ("Update", page.Update), ("About", page.Vm.About),
                     ("Developer", page.Developer), ("Create", page.Create), ("Unknown", page.Create)
                 })
        {
            page.Vm.NavigateCommand.Execute(name);
            Assert.Same(expected, page.Vm.CurrentPage);
            Assert.Equal(name == "Unknown" ? "Create" : name, page.Vm.CurrentPageName);
        }

        var changed = new List<string?>();
        page.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        page.Create.IsBusy = true;
        Assert.True(page.Vm.IsBusy);
        Assert.False(page.Vm.IsIdle);
        Assert.Contains(nameof(MainWindowViewModel.IsIdle), changed);

        page.Vm.NavigateCommand.Execute("Settings");
        page.Vm.ShowUpdateCommand.Execute(null);
        Assert.Same(page.Create, page.Vm.CurrentPage);
        page.Vm.NavigateCommand.Execute("Check");
        Assert.Same(page.Check, page.Vm.CurrentPage);

        page.Create.IsBusy = false;
        page.Vm.ShowUpdateCommand.Execute(null);
        Assert.Same(page.Update, page.Vm.CurrentPage);
    }

    [AvaloniaFact]
    public void UpdateBanner_ShowsAnAvailableUpdate_ExceptOnTheUpdatePage()
    {
        using var page = new MainPage();
        var changed = 0;
        page.Vm.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(MainWindowViewModel.UpdateBannerText) ? 1 : 0;

        Assert.Null(page.Vm.UpdateBannerText);
        page.Update.AvailableVersion = "9.9.9";
        page.Update.State = UpdateState.Available;
        Assert.Equal(TestServices.Localization.Format("Msg_UpdateAvailableBanner", "9.9.9"), page.Vm.UpdateBannerText);

        page.Vm.NavigateCommand.Execute("Update");
        Assert.Null(page.Vm.UpdateBannerText);
        Assert.True(changed >= 2);
    }

    [AvaloniaFact]
    public void ARecoveredSettingsFile_IsAnnounced_UntilDismissed()
    {
        using var page = new MainPage(corruptSettings: true);

        Assert.Equal(TestServices.Localization["Msg_SettingsRecovered"], page.Vm.NoticeText);
        page.Vm.DismissNoticeCommand.Execute(null);
        Assert.Null(page.Vm.NoticeText);
    }

    [AvaloniaFact]
    public void ToggleTheme_SwitchesLightAndDark_UnlessLocked()
    {
        using var page = new MainPage();
        page.Theme.CurrentThemeVariant = AppThemeVariant.Light;

        page.Vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppThemeVariant.Dark, page.Theme.CurrentThemeVariant);
        page.Vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppThemeVariant.Light, page.Theme.CurrentThemeVariant);

        page.Theme.IsThemeLocked = true;
        page.Vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppThemeVariant.Light, page.Theme.CurrentThemeVariant);
    }

    [AvaloniaFact]
    public async Task Activation_OpensThePageThatFitsTheFiles()
    {
        using var page = new MainPage();
        var activated = 0;
        page.Vm.ActivateWindowRequested += (_, _) => activated++;
        var data = page.Temp.File("data.bin", "hello");
        var sums = page.Temp.File("data.bin.sha256", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824  data.bin\n");

        await page.Vm.HandleActivationAsync(new ActivationRequest(ActivationVerb.Auto, [sums]));
        Assert.Same(page.Check, page.Vm.CurrentPage);
        await page.Vm.HandleActivationAsync(new ActivationRequest(ActivationVerb.Auto, [data]));
        Assert.Same(page.Create, page.Vm.CurrentPage);
        await page.Vm.HandleActivationAsync(new ActivationRequest(ActivationVerb.Verify, [data]));
        Assert.Same(page.Check, page.Vm.CurrentPage);

        page.Create.IsBusy = true;
        var waiting = page.Vm.HandleActivationAsync(new ActivationRequest(ActivationVerb.Create, [page.Temp.File("later.bin", "x")]));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        page.Create.IsBusy = false;
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(["data.bin", "later.bin"], page.Create.Items.Select(i => i.FileName).Order(StringComparer.Ordinal));
        Assert.Equal(4, activated);
    }

    [AvaloniaFact]
    public async Task Opening_DeliversQueuedActivations()
    {
        using var page = new MainPage();
        page.Activation.Enqueue(new ActivationRequest(ActivationVerb.Create, [page.Temp.File("queued.bin", "x")]));

        await page.Vm.OnOpenedAsync();

        await Wait.UntilAsync(() => page.Create.Items.Count == 1);
        Assert.Equal("queued.bin", page.Create.Items[0].FileName);
    }

    [AvaloniaFact]
    public async Task Shortcuts_ActOnTheCurrentList()
    {
        using var page = new MainPage();
        page.Storage.Files = [page.Temp.File("a.bin", "hello")];
        page.Storage.Folders = [Directory.CreateDirectory(Path.Combine(page.Temp.Path, "folder")).FullName];
        page.Temp.File("folder/b.bin", "hello");
        var focus = 0;
        page.Create.FocusSearchRequested += (_, _) => focus++;

        await page.Vm.AddFilesCommand.ExecuteAsync(null);
        await page.Vm.AddFolderCommand.ExecuteAsync(null);
        await page.Vm.RunAllCommand.ExecuteAsync(null);
        page.Vm.CancelAllCommand.Execute(null);
        page.Vm.FocusSearchCommand.Execute(null);

        Assert.Equal(2, page.Create.Items.Count);
        Assert.All(page.Create.Items, i => Assert.False(string.IsNullOrEmpty(i.ResultHash)));
        Assert.Equal(1, focus);

        page.Vm.NavigateCommand.Execute("About");
        await page.Vm.AddFilesCommand.ExecuteAsync(null);
        await page.Vm.AddFolderCommand.ExecuteAsync(null);
        await page.Vm.RunAllCommand.ExecuteAsync(null);
        page.Vm.CancelAllCommand.Execute(null);
        page.Vm.FocusSearchCommand.Execute(null);
        Assert.Equal(2, page.Create.Items.Count);
    }

    [AvaloniaFact]
    public void SettingsChanges_UpdateTheWindowFlags()
    {
        using var page = new MainPage();
        var changed = new List<string?>();
        page.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.Settings.Update(s =>
        {
            s.IsDeveloperModeEnabled = true;
            s.IsHashMaskingEnabled = true;
        });

        Assert.True(page.Vm.IsDeveloperModeEnabled);
        Assert.True(page.Vm.IsHashMaskingEnabled);
        Assert.Same(page.Settings.Current, page.Vm.Settings);
        Assert.Contains(nameof(MainWindowViewModel.IsHashMaskingEnabled), changed);
    }
}

public sealed class DeveloperViewModelTests
{
    private sealed class Console : IDisposable
    {
        public Console()
        {
            Logger = Sink.CreateLogger("HashTool.Test");
            Logger.LogInformation("before the console opened");
            Vm = new DeveloperViewModel(Sink, TestServices.Localization, Clipboard, Storage, Launcher);
        }

        public TempDirectory Temp { get; } = new();

        public LogSink Sink { get; } = new(PathSanitizer.Shared);

        public ILogger Logger { get; }

        public RecordingClipboard Clipboard { get; } = new();

        public ScriptedStorage Storage { get; } = new();

        public RecordingLauncher Launcher { get; } = new();

        public DeveloperViewModel Vm { get; }

        public void Dispose()
        {
            Sink.Dispose();
            Temp.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task NewEntries_AppearAndScroll_FilteredByLevel()
    {
        using var console = new Console();
        var appended = 0;
        console.Vm.EntriesAppended += (_, _) => appended++;
        Assert.Single(console.Vm.Entries);

        console.Logger.LogDebug("debug line");
        console.Logger.LogWarning("warning line");
        await Wait.UntilAsync(() => console.Vm.Entries.Count == 3);
        Assert.True(appended > 0);

        console.Vm.MinimumLevel = LogLevel.Warning;
        Assert.Equal("warning line", Assert.Single(console.Vm.Entries).Message);
        console.Logger.LogInformation("filtered out");
        console.Logger.LogError("error line");
        await Wait.UntilAsync(() => console.Vm.Entries.Count == 2);
        Assert.Contains(LogLevel.Error, console.Vm.Levels);
    }

    [AvaloniaFact]
    public async Task TheConsole_KeepsAtMostTheSinkCapacity()
    {
        using var console = new Console();
        console.Vm.AutoScroll = false;

        for (var i = 0; i <= LogSink.Capacity; i++) console.Logger.LogInformation("entry {Index}", i);

        await Wait.UntilAsync(() => console.Vm.Entries.Count == LogSink.Capacity && console.Vm.Entries[^1].Message == $"entry {LogSink.Capacity}");
        Assert.Equal("entry 1", console.Vm.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task Commands_PauseClearCopyExportAndOpenTheFolder()
    {
        using var console = new Console();

        console.Vm.IsRecording = false;
        console.Logger.LogWarning("not recorded");
        Assert.False(console.Sink.IsRecording);
        console.Vm.IsRecording = true;

        await console.Vm.CopyCommand.ExecuteAsync(null);
        Assert.Contains("before the console opened", Assert.Single(console.Clipboard.Texts), StringComparison.Ordinal);

        await console.Vm.ExportCommand.ExecuteAsync(null);
        console.Storage.SavePath = Path.Combine(console.Temp.Path, "log.txt");
        await console.Vm.ExportCommand.ExecuteAsync(null);
        Assert.Contains("before the console opened", (await File.ReadAllTextAsync(console.Storage.SavePath, TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.StartsWith("hashtool-log-", console.Storage.SuggestedName, StringComparison.Ordinal);

        await console.Vm.OpenLogFolderCommand.ExecuteAsync(null);
        Assert.Equal([AppPaths.LogDirectory], console.Launcher.Folders);

        console.Vm.ClearCommand.Execute(null);
        Assert.Empty(console.Vm.Entries);
        Assert.Empty(console.Sink.Snapshot());
    }
}

public sealed class AboutViewModelTests
{
    [AvaloniaFact]
    public async Task Links_OpenInTheBrowser()
    {
        var launcher = new RecordingLauncher();
        var about = new AboutViewModel(AppHost.Get<UpdateService>(), TestServices.Localization, launcher);

        await about.OpenUrlCommand.ExecuteAsync("https://github.com/poli0981/HashTool");
        await about.OpenUrlCommand.ExecuteAsync("");
        await about.OpenUrlCommand.ExecuteAsync(null);

        Assert.Equal(["https://github.com/poli0981/HashTool"], launcher.Urls);
        Assert.All(about.Libraries, l => Assert.False(string.IsNullOrEmpty(l.Version)));
    }
}
