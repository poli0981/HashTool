using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CheckHash.Models;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern void malloc_zone_pressure_relief(IntPtr zone, ulong goal);

    // Config Path
    [ObservableProperty] private string _configFilePath;

    [ObservableProperty] private List<AppThemeStyle> _filteredThemeStyles = new();
    [ObservableProperty] private int _forceQuitTimeout;

    [ObservableProperty] private bool _isAdminModeEnabled;

    [ObservableProperty] private bool _isDeveloperModeEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeFont))]
    [NotifyPropertyChangedFor(nameof(CanChangeTheme))]
    private bool _isSettingsLocked;

    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);
    [ObservableProperty] private bool _showConfigPath;

    [ObservableProperty] private bool _showReadWriteSpeed;

    private bool _showLanguageChangeWarning = true;

    public LanguageItem SelectedLanguage
    {
        get => Localization.SelectedLanguage;
        set
        {
            if (value == null || value == Localization.SelectedLanguage) return;

            if (value.Code == "auto")
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (_showLanguageChangeWarning)
                    {
                        var (confirmed, isChecked) = await MessageBoxHelper.ShowConfirmationWithCheckboxAsync(
                            L["Msg_LanguageChangeTitle"],
                            L["Msg_LanguageChangeConfirmation"],
                            L["Msg_DontShowAgain"],
                            L["Btn_Yes"],
                            L["Btn_No"],
                            MessageBoxIcon.Question);

                        if (!confirmed)
                        {
                            OnPropertyChanged(nameof(SelectedLanguage));
                            return;
                        }

                        if (isChecked)
                        {
                            _showLanguageChangeWarning = false;
                        }
                    }

                    Localization.SelectedLanguage = value;
                    OnPropertyChanged(nameof(CanSetLanguageDefault));
                    await SaveSettingsAsync();
                });
            }
            else
            {
                Localization.SelectedLanguage = value;
                OnPropertyChanged(nameof(CanSetLanguageDefault));
            }
        }
    }

    private bool _isInitializing;

    public SettingsViewModel()
    {
        UpdateFilteredThemes();

        Theme.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Theme.CurrentThemeVariant))
            {
                UpdateFilteredThemes();
            }
            else if (e.PropertyName == nameof(Theme.IsThemeLocked))
            {
                OnPropertyChanged(nameof(CanChangeTheme));
            }

            if (_isInitializing) return;

            if (e.PropertyName == nameof(Theme.CurrentThemeVariant))
            {
                Logger.Log($"Theme variant changed to {Theme.CurrentThemeVariant}");
            }
            else if (e.PropertyName == nameof(Theme.CurrentThemeStyle))
            {
                Logger.Log($"Theme style changed to {Theme.CurrentThemeStyle}");
            }
            else if (e.PropertyName == nameof(Theme.IsThemeLocked))
            {
                _ = SaveSettingsAsync();
                Logger.Log($"Theme lock changed: {Theme.IsThemeLocked}");
            }
        };

        Font.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Font.IsLockedFont)) OnPropertyChanged(nameof(CanChangeFont));
        };

        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LocalizationService.Instance.SelectedLanguage))
            {
                OnPropertyChanged(nameof(CanSetLanguageDefault));
                OnPropertyChanged(nameof(SelectedLanguage));
                if (!_isInitializing)
                    Logger.Log($"Language changed to {LocalizationService.Instance.SelectedLanguage.Code}");
            }
            else if (e.PropertyName == "Item[]")
            {
                Localization = new LocalizationProxy(LocalizationService.Instance);
            }
        };

        Prefs.PropertyChanged += (s, e) =>
        {
            if (_isInitializing) return;

            if (!IsSettingsLocked)
            {
                _ = SaveSettingsAsync();
                Logger.Log($"Preference changed: {e.PropertyName}");
            }
        };

        ConfigFilePath = ConfigService.ConfigPath;
        _ = LoadSettingsAsync();
    }

    private LocalizationService L => LocalizationService.Instance;
    public FontService Font => FontService.Instance;
    public PreferencesService Prefs => PreferencesService.Instance;
    public ThemeService Theme => ThemeService.Instance;
    private ConfigurationService ConfigService => ConfigurationService.Instance;
    private LoggerService Logger => LoggerService.Instance;

    public bool CanSetLanguageDefault => Localization.SelectedLanguage.Code != "auto";
    public bool CanChangeFont => !IsSettingsLocked && !Font.IsLockedFont;
    public bool CanChangeTheme => !IsSettingsLocked && !Theme.IsThemeLocked;

    public List<FileSizeUnit> FileSizeUnits { get; } =
        Enum.GetValues(typeof(FileSizeUnit)).Cast<FileSizeUnit>().ToList();

    public bool IsAdminModeSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private void UpdateFilteredThemes()
    {
        FilteredThemeStyles = Theme.GetAvailableThemesForVariant(Theme.CurrentThemeVariant);
        if (!FilteredThemeStyles.Contains(Theme.CurrentThemeStyle)) Theme.CurrentThemeStyle = AppThemeStyle.Fluent;
    }

    [RelayCommand]
    private async Task ResetSettings()
    {
        if (IsSettingsLocked) return;

        var defaultConfig = new AppConfig();

        // Reset Appearance
        Font.ResetSettings();
        Theme.CurrentThemeStyle = defaultConfig.ThemeStyle;
        Theme.CurrentThemeVariant = defaultConfig.ThemeVariant;
        Theme.IsThemeLocked = defaultConfig.IsThemeLocked;

        // Reset Language
        Localization.SelectedLanguage =
            Localization.AvailableLanguages.FirstOrDefault(x => x.Code == defaultConfig.LanguageCode) ??
            Localization.AvailableLanguages[0];
        _showLanguageChangeWarning = defaultConfig.ShowLanguageChangeWarning;

        // Reset Developer & Admin Mode
        IsDeveloperModeEnabled = defaultConfig.IsDeveloperModeEnabled;
        IsAdminModeEnabled = defaultConfig.IsAdminModeEnabled;

        // Reset Timeouts & Monitor
        ForceQuitTimeout = defaultConfig.ForceQuitTimeout;
        ShowReadWriteSpeed = defaultConfig.ShowReadWriteSpeed;

        // Reset Preferences
        Prefs.IsHashMaskingEnabled = defaultConfig.IsHashMaskingEnabled;

        Prefs.IsFileSizeLimitEnabled = defaultConfig.IsFileSizeLimitEnabled;
        Prefs.FileSizeLimitValue = defaultConfig.FileSizeLimitValue;
        Prefs.FileSizeLimitUnit = defaultConfig.FileSizeLimitUnit;

        Prefs.IsFileTimeoutEnabled = defaultConfig.IsFileTimeoutEnabled;
        Prefs.FileTimeoutSeconds = defaultConfig.FileTimeoutSeconds;

        Prefs.IsMaxFileCountEnabled = defaultConfig.IsMaxFileCountEnabled;
        Prefs.MaxFileCount = defaultConfig.MaxFileCount;

        Prefs.IsMaxFolderCountEnabled = defaultConfig.IsMaxFolderCountEnabled;
        Prefs.MaxFolderCount = defaultConfig.MaxFolderCount;

        await SaveSettingsAsync();
        Logger.Log("All settings reset to default.", LogLevel.Warning);
    }

    [RelayCommand]
    private void SetVariantSystem()
    {
        if (!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.System;
    }

    [RelayCommand]
    private void SetVariantLight()
    {
        if (!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.Light;
    }

    [RelayCommand]
    private void SetVariantDark()
    {
        if (!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.Dark;
    }

    [RelayCommand]
    private async Task ToggleLockSettings()
    {
        if (IsSettingsLocked)
        {
            await SaveSettingsAsync();
            Logger.Log("Settings locked.");
        }
        else
        {
            Logger.Log("Settings unlocked.");
        }
    }

    [RelayCommand]
    private async Task ToggleDeveloperMode()
    {
        await SaveSettingsAsync();
        Logger.Log($"Developer Mode: {IsDeveloperModeEnabled}");
    }

    [RelayCommand]
    private async Task SetCurrentLanguageAsDefault()
    {
        if (IsSettingsLocked) return;

        if (_showLanguageChangeWarning)
        {
            var (confirmed, isChecked) = await MessageBoxHelper.ShowConfirmationWithCheckboxAsync(
                L["Msg_LanguageChangeTitle"],
                L["Msg_LanguageChangeConfirmation"],
                L["Msg_DontShowAgain"],
                L["Btn_Yes"],
                L["Btn_No"],
                MessageBoxIcon.Question);

            if (!confirmed) return;

            if (isChecked)
            {
                _showLanguageChangeWarning = false;
            }
        }

        await SaveSettingsAsync();
        Logger.Log("Language saved as default.");
    }

    [RelayCommand]
    private async Task CheckConfigFile()
    {
        var path = ConfigService.ConfigPath;
        if (File.Exists(path))
            await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigExists"].Replace("{0}", path), MessageBoxIcon.Information);
        else
            await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigMissing"], MessageBoxIcon.Warning);
    }

    [RelayCommand]
    private async Task CopyConfigPath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ConfigFilePath);
                await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigCopied"], MessageBoxIcon.Success);
                Logger.Log("Config path copied to clipboard.");
            }
        }
    }

    [RelayCommand]
    private void ForceQuitAndCancelAll()
    {
        Logger.Log("Force Quit initiated by user.", LogLevel.Error);
        Prefs.RequestForceCancel();
        Environment.Exit(0);
    }

    [RelayCommand]
    private async Task ToggleAdminMode()
    {
        await SaveSettingsAsync();
        Logger.Log($"Admin Mode toggled: {IsAdminModeEnabled}");
        if (IsAdminModeEnabled) await MessageBoxHelper.ShowAsync(L["Msg_AdminMode"], L["Msg_AdminRestart"], MessageBoxIcon.Warning);
    }

    [RelayCommand]
    private void FreeMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(proc.Handle, -1, -1);
            }
            catch
            {
                // Ignore errors during working set trimming
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                malloc_zone_pressure_relief(IntPtr.Zero, 0);
            }
            catch
            {
                // Ignore errors
            }
        }

        Logger.Log(L["Msg_MemoryFreed"], LogLevel.Success);
    }

    partial void OnForceQuitTimeoutChanged(int value)
    {
        if (!_isInitializing) _ = SaveSettingsAsync();
    }

    partial void OnShowReadWriteSpeedChanged(bool value)
    {
        if (!_isInitializing) _ = SaveSettingsAsync();
    }

    partial void OnIsAdminModeEnabledChanged(bool value)
    {
        if (!_isInitializing) _ = SaveSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        _isInitializing = true;
        try
        {
            var config = await ConfigService.LoadAsync();

            IsSettingsLocked = config.IsSettingsLocked;
            IsDeveloperModeEnabled = config.IsDeveloperModeEnabled;

            var lang = Localization.AvailableLanguages.FirstOrDefault(x => x.Code == config.LanguageCode);
            if (lang != null)
            {
                Localization.SelectedLanguage = lang;
                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(CanSetLanguageDefault));
            }

            Theme.CurrentThemeStyle = config.ThemeStyle;
            Theme.CurrentThemeVariant = config.ThemeVariant;
            Theme.IsThemeLocked = config.IsThemeLocked;

            if (!string.IsNullOrEmpty(config.FontFamily))
            {
                var font = Font.InstalledFonts.FirstOrDefault(x => x.Name == config.FontFamily);
                if (font != null) Font.SelectedFont = font;
            }

            Font.BaseFontSize = config.BaseFontSize;
            Font.UiScale = config.UiScale;
            Font.IsLockedFont = config.IsFontLocked;
            Font.IsAutoFont = config.IsAutoFont;

            Prefs.IsHashMaskingEnabled = config.IsHashMaskingEnabled;

            Prefs.IsFileSizeLimitEnabled = config.IsFileSizeLimitEnabled;
            Prefs.FileSizeLimitValue = config.FileSizeLimitValue;
            Prefs.FileSizeLimitUnit = config.FileSizeLimitUnit;

            Prefs.IsFileTimeoutEnabled = config.IsFileTimeoutEnabled;
            Prefs.FileTimeoutSeconds = config.FileTimeoutSeconds;

            Prefs.IsMaxFileCountEnabled = config.IsMaxFileCountEnabled;
            Prefs.MaxFileCount = config.MaxFileCount;

            Prefs.IsMaxFolderCountEnabled = config.IsMaxFolderCountEnabled;
            Prefs.MaxFolderCount = config.MaxFolderCount;

            IsAdminModeEnabled = config.IsAdminModeEnabled;
            ForceQuitTimeout = config.ForceQuitTimeout;
            _showLanguageChangeWarning = config.ShowLanguageChangeWarning;
            ShowReadWriteSpeed = config.ShowReadWriteSpeed;

            Logger.Log("Settings loaded from config.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading settings: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public async Task SaveSettingsAsync()
    {
        var config = new AppConfig
        {
            IsSettingsLocked = IsSettingsLocked,
            IsDeveloperModeEnabled = IsDeveloperModeEnabled,
            LanguageCode = Localization.SelectedLanguage.Code,
            ThemeStyle = Theme.CurrentThemeStyle,
            ThemeVariant = Theme.CurrentThemeVariant,
            IsThemeLocked = Theme.IsThemeLocked,
            FontFamily = Font.SelectedFont?.Name,
            BaseFontSize = Font.BaseFontSize,
            UiScale = Font.UiScale,
            IsFontLocked = Font.IsLockedFont,
            IsAutoFont = Font.IsAutoFont,
            IsHashMaskingEnabled = Prefs.IsHashMaskingEnabled,
            IsFileSizeLimitEnabled = Prefs.IsFileSizeLimitEnabled,
            FileSizeLimitValue = Prefs.FileSizeLimitValue,
            FileSizeLimitUnit = Prefs.FileSizeLimitUnit,
            IsFileTimeoutEnabled = Prefs.IsFileTimeoutEnabled,
            FileTimeoutSeconds = Prefs.FileTimeoutSeconds,
            IsMaxFileCountEnabled = Prefs.IsMaxFileCountEnabled,
            MaxFileCount = Prefs.MaxFileCount,
            IsMaxFolderCountEnabled = Prefs.IsMaxFolderCountEnabled,
            MaxFolderCount = Prefs.MaxFolderCount,
            IsAdminModeEnabled = IsAdminModeEnabled,
            ForceQuitTimeout = ForceQuitTimeout,

            ShowLanguageChangeWarning = _showLanguageChangeWarning,
            ShowReadWriteSpeed = ShowReadWriteSpeed
        };

        await ConfigService.SaveAsync(config);
        Logger.Log("Settings saved.");
    }
}