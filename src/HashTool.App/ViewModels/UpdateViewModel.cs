using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Services;
using Microsoft.Extensions.Logging;
using Velopack;

namespace HashTool.App.ViewModels;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    NotInstalled,
    Error
}

/// <summary>The Update page and the once-a-day background check.</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateService _updates;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localizationService;
    private readonly ILauncherService _launcher;
    private readonly ILogger<UpdateViewModel> _logger;
    private UpdateInfo? _update;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private LocalizationProxy _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsChecking), nameof(IsDownloading), nameof(CanInstall), nameof(CanCheck))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand), nameof(InstallOnExitCommand))]
    private UpdateState _state = UpdateState.Idle;

    [ObservableProperty] private string? _errorDetail;
    [ObservableProperty] private string? _availableVersion;
    [ObservableProperty] private string? _releaseNotes;
    [ObservableProperty] private int _downloadProgress;

    public UpdateViewModel(UpdateService updates, SettingsService settings, LocalizationService localization, ILauncherService launcher,
        ILogger<UpdateViewModel> logger)
    {
        _updates = updates;
        _settings = settings;
        _localizationService = localization;
        _launcher = launcher;
        _logger = logger;
        _localization = new LocalizationProxy(localization);
        localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CurrentVersionText));
        };
        // The same option is also on the Settings page.
        settings.Changed += (_, _) => OnPropertyChanged(nameof(CheckOnStartup));
        if (!updates.IsInstalled) _state = UpdateState.NotInstalled;
    }

    /// <summary>Raised when an update is found by the background check (the main window shows a banner).</summary>
    public event EventHandler? UpdateFound;

    /// <summary>Raised right before the app exits to install an update, so the window can save its state.</summary>
    public event EventHandler? RestartRequested;

    private LocalizationService L => _localizationService;

    public string CurrentVersionText => L.Format("Lbl_CurrentVersion", _updates.CurrentVersion);

    public bool IsChecking => State == UpdateState.Checking;

    public bool IsDownloading => State == UpdateState.Downloading;

    public bool CanCheck => State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.NotInstalled);

    public bool CanInstall => State is UpdateState.Available or UpdateState.ReadyToInstall;

    public bool CheckOnStartup
    {
        get => _settings.Current.CheckForUpdatesOnStartup;
        set
        {
            _settings.Update(s => s.CheckForUpdatesOnStartup = value);
            OnPropertyChanged();
        }
    }

    public string StatusText => State switch
    {
        UpdateState.Checking => L["Status_Checking"],
        UpdateState.UpToDate => L["Status_Latest"],
        UpdateState.Available => L.Format("Msg_UpdateAvailableBanner", AvailableVersion),
        UpdateState.Downloading => L.Format("Status_Downloading", DownloadProgress),
        UpdateState.ReadyToInstall => L["Status_ReadyToInstall"],
        UpdateState.NotInstalled => L["Msg_UpdatesUnavailable"],
        UpdateState.Error => L.Format("Status_CheckError", ErrorDetail),
        _ => L["Lbl_Status_Ready"]
    };

    partial void OnDownloadProgressChanged(int value) => OnPropertyChanged(nameof(StatusText));

    /// <summary>Checks quietly at most once a day when enabled (never in CI or smoke tests).</summary>
    public async Task CheckInBackgroundIfDueAsync()
    {
        var settings = _settings.Current;
        if (!settings.CheckForUpdatesOnStartup || !_updates.IsInstalled || Environment.GetEnvironmentVariable("CI") is not null) return;
        if (settings.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < AutomaticCheckInterval) return;

        await CheckCoreAsync(interactive: false);
        if (State == UpdateState.Available) UpdateFound?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync() => CheckCoreAsync(interactive: true);

    private async Task CheckCoreAsync(bool interactive)
    {
        State = UpdateState.Checking;
        ErrorDetail = null;
        UpdateCheckResult result;
        try
        {
            // A check is short and not cancellable from the page.
            result = await _updates.CheckAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never leave the page stuck on "Checking".
            _logger.LogError(ex, "Update check failed");
            result = new UpdateCheckResult(UpdateCheckStatus.Failed, Error: ex);
        }

        _settings.Update(s => s.LastUpdateCheck = DateTimeOffset.UtcNow);

        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                State = UpdateState.UpToDate;
                break;
            case UpdateCheckStatus.UpdateAvailable:
                _update = result.Update;
                AvailableVersion = result.Version;
                ReleaseNotes = ToPlainText(result.ReleaseNotes);
                State = UpdateState.Available;
                break;
            case UpdateCheckStatus.NotInstalled:
                State = UpdateState.NotInstalled;
                break;
            default:
                ErrorDetail = result.Status switch
                {
                    UpdateCheckStatus.NetworkError => L["Error_Network"],
                    UpdateCheckStatus.RateLimited => L["Error_ApiLimit"],
                    _ => result.Error?.Message
                };
                State = UpdateState.Error;
                if (!interactive) _logger.LogInformation("Background update check failed: {Status}", result.Status);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (await DownloadAsync() && _update is not null)
        {
            // Velopack exits the process immediately: save what normally is saved on close.
            RestartRequested?.Invoke(this, EventArgs.Empty);
            _settings.Flush();
            _updates.ApplyAndRestart(_update);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallOnExitAsync()
    {
        if (await DownloadAsync() && _update is not null)
        {
            _updates.ApplyOnExit(_update);
            State = UpdateState.ReadyToInstall;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private Task OpenReleasePageAsync() =>
        _launcher.OpenUrlAsync(AvailableVersion is null
            ? $"{UpdateService.RepositoryUrl}/releases"
            : $"{UpdateService.RepositoryUrl}/releases/tag/{Uri.EscapeDataString(AvailableVersion)}");

    private async Task<bool> DownloadAsync()
    {
        if (_update is null) return false;
        if (State == UpdateState.ReadyToInstall) return true;

        State = UpdateState.Downloading;
        DownloadProgress = 0;
        _downloadCts = new CancellationTokenSource();
        try
        {
            // Velopack reports progress from a background thread.
            await _updates.DownloadAsync(_update, p => Dispatcher.UIThread.Post(() => DownloadProgress = p), _downloadCts.Token);
            State = UpdateState.ReadyToInstall;
            return true;
        }
        catch (OperationCanceledException)
        {
            State = UpdateState.Available;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Downloading the update failed");
            ErrorDetail = ex.Message;
            State = UpdateState.Error;
            return false;
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    /// <summary>Release notes are Markdown; show them as readable plain text.</summary>
    internal static string? ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        try
        {
            text = Heading().Replace(text, "");
            text = Bullet().Replace(text, "• ");
            text = Bold().Replace(text, "${a}${b}");
            text = Code().Replace(text, "${code}");
            text = Link().Replace(text, "${text}");
            text = BlankLines().Replace(text, "\n\n");
        }
        catch (RegexMatchTimeoutException)
        {
            // Notes come from the network; show them unformatted rather than hang on a pathological input.
        }

        return text.Trim();
    }

    // [ \t] rather than \s: \s also matches line breaks, which swallowed the blank line before a list.
    [GeneratedRegex(@"^#{1,6}[ \t]*", RegexOptions.Multiline, 1000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline, 1000)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\*\*(?<a>.+?)\*\*|__(?<b>.+?)__", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex Bold();

    [GeneratedRegex(@"`(?<code>[^`]+)`", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex Code();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\([^)]+\)", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex Link();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, 1000)]
    private static partial Regex BlankLines();
}
