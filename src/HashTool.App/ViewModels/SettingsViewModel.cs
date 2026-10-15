using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.Core.Checksums;
using HashTool.Core.Configuration;
using HashTool.Core.Hashing;
using Microsoft.Extensions.Logging;

namespace HashTool.App.ViewModels;

/// <summary>Something displayed through a translation key (see <c>EnumLabelConverter</c>).</summary>
public interface ILabeled
{
    string LabelKey { get; }
}

/// <summary>A ComboBox option whose label is a translation key.</summary>
public sealed record Option<T>(T Value, string LabelKey) : ILabeled;

/// <summary>Minimum level written to the log file; Developer Mode lowers it to Debug.</summary>
public sealed class LogLevelSwitch
{
    public LogLevel Minimum { get; set; } = LogLevel.Information;
}

/// <summary>The Settings page. Every change is saved immediately (debounced) through <see cref="SettingsService"/>.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly ILauncherService _launcher;
    private readonly UpdateService _updates;
    private readonly LogLevelSwitch _logLevel;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _applying;

    [ObservableProperty] private LocalizationProxy _localization;
    [ObservableProperty] private bool _showConfigPath;

    public SettingsViewModel(SettingsService settings, LocalizationService localization, FontService font, ThemeService theme,
        IDialogService dialogs, IClipboardService clipboard, ILauncherService launcher, UpdateService updates, LogLevelSwitch logLevel,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        Loc = localization;
        Font = font;
        Theme = theme;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _launcher = launcher;
        _updates = updates;
        _logLevel = logLevel;
        _logger = logger;
        _localization = new LocalizationProxy(localization);

        localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(ParallelFilesHint));
        };
        font.PropertyChanged += OnFontChanged;
        theme.PropertyChanged += OnThemeChanged;
        // The update check option is also on the Update page.
        settings.Changed += (_, _) => OnPropertyChanged(nameof(CheckForUpdatesOnStartup));
    }

    public LocalizationService Loc { get; }

    public FontService Font { get; }

    public ThemeService Theme { get; }

    private AppSettings S => _settings.Current;

    public string ConfigFilePath => _settings.FilePath;

    public IReadOnlyList<AppThemeStyle> ThemeStyles { get; } = ThemeService.Styles;

    public IReadOnlyList<FileSizeUnit> FileSizeUnits { get; } = Enum.GetValues<FileSizeUnit>();

    public IReadOnlyList<Option<HexCase>> HexCaseOptions { get; } =
        [new(HexCase.Upper, "Settings_HexUpper"), new(HexCase.Lower, "Settings_HexLower")];

    public IReadOnlyList<Option<ExportFormat>> ExportFormatOptions { get; } =
    [
        new(ExportFormat.Gnu, "FileType_Gnu"), new(ExportFormat.Bsd, "FileType_Bsd"), new(ExportFormat.Sfv, "FileType_Sfv"),
        new(ExportFormat.Json, "FileType_Json"), new(ExportFormat.Csv, "FileType_Csv")
    ];

    public bool CanEdit => !S.IsSettingsLocked;

    public bool CanChangeTheme => CanEdit && !Theme.IsThemeLocked;

    public bool CanChangeFont => CanEdit && !Font.IsLockedFont;

    public int AutomaticParallelism => new Core.Processing.BatchOptions().EffectiveParallelism;

    /// <summary>"0 = automatic (N on this computer)".</summary>
    public string ParallelFilesHint => Loc.Format("Settings_ParallelFilesDesc", AutomaticParallelism);

    // ------------------------------------------------------------------ language & appearance

    public LanguageItem SelectedLanguage
    {
        get => Loc.SelectedLanguage;
        set
        {
            if (value is null || Equals(value, Loc.SelectedLanguage)) return;
            Loc.SelectedLanguage = value;
            Font.ApplyAutomaticFont(Loc.Culture);
            _settings.Update(s => s.LanguageCode = value.Code);
            OnPropertyChanged();
            _logger.LogInformation("Language changed to {Code}", value.Code);
        }
    }

    [RelayCommand]
    private void SetVariant(AppThemeVariant variant)
    {
        if (CanChangeTheme) Theme.CurrentThemeVariant = variant;
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying) return;
        switch (e.PropertyName)
        {
            case nameof(ThemeService.CurrentThemeStyle):
                _settings.Update(s => s.ThemeStyle = Theme.CurrentThemeStyle);
                break;
            case nameof(ThemeService.CurrentThemeVariant):
                _settings.Update(s => s.ThemeVariant = Theme.CurrentThemeVariant);
                break;
            case nameof(ThemeService.IsThemeLocked):
                _settings.Update(s => s.IsThemeLocked = Theme.IsThemeLocked);
                OnPropertyChanged(nameof(CanChangeTheme));
                break;
        }
    }

    private void OnFontChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying) return;
        switch (e.PropertyName)
        {
            case nameof(FontService.SelectedFont):
                _settings.Update(s => s.FontFamily = Font.SelectedFontName);
                break;
            case nameof(FontService.BaseFontSize):
                _settings.Update(s => s.BaseFontSize = Font.BaseFontSize);
                break;
            case nameof(FontService.UiScale):
                _settings.Update(s => s.UiScale = Font.UiScale);
                break;
            case nameof(FontService.IsAutoFont):
                _settings.Update(s => s.IsAutoFont = Font.IsAutoFont);
                if (Font.IsAutoFont) Font.ApplyAutomaticFont(Loc.Culture);
                break;
            case nameof(FontService.IsLockedFont):
                _settings.Update(s => s.IsFontLocked = Font.IsLockedFont);
                OnPropertyChanged(nameof(CanChangeFont));
                break;
        }
    }

    // ------------------------------------------------------------------ processing

    public bool IsFileSizeLimitEnabled { get => S.IsFileSizeLimitEnabled; set => Set(s => s.IsFileSizeLimitEnabled = value); }
    public decimal FileSizeLimitValue { get => (decimal)S.FileSizeLimitValue; set => Set(s => s.FileSizeLimitValue = (double)value); }
    public FileSizeUnit FileSizeLimitUnit { get => S.FileSizeLimitUnit; set => Set(s => s.FileSizeLimitUnit = value); }
    public bool IsFileTimeoutEnabled { get => S.IsFileTimeoutEnabled; set => Set(s => s.IsFileTimeoutEnabled = value); }
    public decimal FileTimeoutSeconds { get => S.FileTimeoutSeconds; set => Set(s => s.FileTimeoutSeconds = (int)value); }
    public bool IsMaxFileCountEnabled { get => S.IsMaxFileCountEnabled; set => Set(s => s.IsMaxFileCountEnabled = value); }
    public decimal MaxFileCount { get => S.MaxFileCount; set => Set(s => s.MaxFileCount = (int)value); }
    public bool IsMaxFolderCountEnabled { get => S.IsMaxFolderCountEnabled; set => Set(s => s.IsMaxFolderCountEnabled = value); }
    public decimal MaxFolderCount { get => S.MaxFolderCount; set => Set(s => s.MaxFolderCount = (int)value); }
    public decimal MaxParallelFiles { get => S.MaxParallelFiles; set => Set(s => s.MaxParallelFiles = (int)value); }
    public bool OptimizeForHdd { get => S.OptimizeForHdd; set => Set(s => s.OptimizeForHdd = value); }
    public bool IncludeHiddenFiles { get => S.IncludeHiddenFiles; set => Set(s => s.IncludeHiddenFiles = value); }

    // ------------------------------------------------------------------ output, privacy, updates

    public Option<HexCase> HashLetterCase
    {
        get => HexCaseOptions.First(o => o.Value == S.HashLetterCase);
        set => Set(s => s.HashLetterCase = value.Value);
    }

    public Option<ExportFormat> DefaultExportFormat
    {
        get => ExportFormatOptions.First(o => o.Value == S.DefaultExportFormat);
        set => Set(s => s.DefaultExportFormat = value.Value);
    }

    /// <summary>Deliberately stays editable while settings are locked (see Settings_LockDesc).</summary>
    public bool IsHashMaskingEnabled { get => S.IsHashMaskingEnabled; set => Set(s => s.IsHashMaskingEnabled = value); }

    public bool ShowReadWriteSpeed { get => S.ShowReadWriteSpeed; set => Set(s => s.ShowReadWriteSpeed = value); }

    public bool CheckForUpdatesOnStartup { get => S.CheckForUpdatesOnStartup; set => Set(s => s.CheckForUpdatesOnStartup = value); }

    public bool IsDeveloperModeEnabled
    {
        get => S.IsDeveloperModeEnabled;
        set
        {
            Set(s => s.IsDeveloperModeEnabled = value);
            _logLevel.Minimum = value ? LogLevel.Debug : LogLevel.Information;
        }
    }

    public bool IsSettingsLocked
    {
        get => S.IsSettingsLocked;
        set
        {
            Set(s => s.IsSettingsLocked = value);
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanChangeTheme));
            OnPropertyChanged(nameof(CanChangeFont));
        }
    }

    // ------------------------------------------------------------------ system integration (Windows)

    public bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Only installed or portable builds register shell entries (never a development build's temporary path).</summary>
    public bool CanUseShellIntegration => ShellIntegration.IsSupported && !Elevation.IsElevated &&
                                          (_updates.IsInstalled || _updates.IsPortable ||
                                           Environment.GetEnvironmentVariable("HASHTOOL_ALLOW_SHELL") == "1");

    public bool IsShellIntegrationEnabled
    {
        get => S.IsShellIntegrationEnabled;
        set
        {
            if (value == S.IsShellIntegrationEnabled) return;
            try
            {
                if (value) RegisterShell();
                else ShellIntegration.Unregister();
                Set(s => s.IsShellIntegrationEnabled = value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Changing the Explorer integration failed");
                _ = _dialogs.ShowErrorAsync(Loc["Msg_Error"], Loc.Format("Msg_UnexpectedError", ex.Message), ex.ToString());
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Re-registers the context menu (new labels after a language change, or a moved portable folder).</summary>
    public void RefreshShellIntegration()
    {
        if (!S.IsShellIntegrationEnabled || !CanUseShellIntegration) return;
        try
        {
            RegisterShell();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing the Explorer integration failed");
        }
    }

    private void RegisterShell()
    {
        if (Environment.ProcessPath is { } exe) ShellIntegration.Register(exe, Loc["Shell_Create"], Loc["Shell_Verify"]);
    }

    public bool CanRestartElevated => Elevation.CanRestartElevated;

    public bool IsElevated => Elevation.IsElevated;

    /// <summary>Raised after an elevated copy has been started; the app should close.</summary>
    public event EventHandler? RequestShutdown;

    [RelayCommand]
    private void RestartElevated()
    {
        if (Elevation.RestartElevated(ActivationVerb.Auto, [])) RequestShutdown?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ configuration

    [RelayCommand]
    private async Task CopyConfigPathAsync()
    {
        await _clipboard.SetTextAsync(ConfigFilePath);
        await _dialogs.ShowAsync(Loc["Msg_ConfigCheck"], Loc["Msg_ConfigCopied"], DialogIcon.Success);
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => _launcher.OpenFolderAsync(AppPaths.DataDirectory);

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        if (!CanEdit) return;
        if (!await _dialogs.ConfirmAsync(Loc["Settings_ResetAll"], Loc["Settings_ResetDesc"], DialogIcon.Warning)) return;

        var defaults = new AppSettings();
        var keepShell = S.IsShellIntegrationEnabled;
        _settings.Update(s =>
        {
            foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
                property.SetValue(s, property.GetValue(defaults));
            s.IsShellIntegrationEnabled = keepShell;
        });
        Apply();
        _logger.LogWarning("All settings were reset to defaults");
    }

    /// <summary>Pushes the stored settings into the live services (startup and reset).</summary>
    public void Apply()
    {
        _applying = true;
        try
        {
            Loc.SelectedLanguage = Loc.Find(S.LanguageCode);
            Theme.IsThemeLocked = S.IsThemeLocked;
            Theme.CurrentThemeStyle = S.ThemeStyle;
            Theme.CurrentThemeVariant = S.ThemeVariant;
            Font.BaseFontSize = S.BaseFontSize;
            Font.UiScale = S.UiScale;
            Font.IsLockedFont = S.IsFontLocked;
            Font.IsAutoFont = S.IsAutoFont;
            if (S.IsAutoFont) Font.ApplyAutomaticFont(Loc.Culture);
            else Font.SelectFont(S.FontFamily);
            _logLevel.Minimum = S.IsDeveloperModeEnabled ? LogLevel.Debug : LogLevel.Information;
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(string.Empty);
    }

    private void Set(Action<AppSettings> change, [CallerMemberName] string? name = null)
    {
        _settings.Update(change);
        OnPropertyChanged(name);
    }
}
