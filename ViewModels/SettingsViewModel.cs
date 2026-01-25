using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CheckHash.Services;
using CheckHash.Models;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace CheckHash.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public LocalizationService Localization => LocalizationService.Instance;
    private LocalizationService L => LocalizationService.Instance;
    public FontService Font => FontService.Instance;
    public PreferencesService Prefs => PreferencesService.Instance;
    public ThemeService Theme => ThemeService.Instance;
    private ConfigurationService ConfigService => ConfigurationService.Instance;
    private LoggerService Logger => LoggerService.Instance;

    [ObservableProperty] private List<AppThemeStyle> _filteredThemeStyles;
    
    [ObservableProperty] private bool _isSettingsLocked;
    [ObservableProperty] private bool _isDeveloperModeEnabled;
    
    public bool CanSetLanguageDefault => Localization.SelectedLanguage.Code != "auto";

    public List<FileSizeUnit> FileSizeUnits { get; } = Enum.GetValues(typeof(FileSizeUnit)).Cast<FileSizeUnit>().ToList();

    [ObservableProperty] private bool _isAdminModeEnabled;
    [ObservableProperty] private int _forceQuitTimeout;
    
    public bool IsAdminModeSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Config Path
    [ObservableProperty] private string _configFilePath;
    [ObservableProperty] private bool _showConfigPath;

    public SettingsViewModel()
    {
        UpdateFilteredThemes();
        
        Theme.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Theme.CurrentThemeVariant))
            {
                UpdateFilteredThemes();
                Logger.Log($"Theme variant changed to {Theme.CurrentThemeVariant}");
            }
            else if (e.PropertyName == nameof(Theme.CurrentThemeStyle))
            {
                Logger.Log($"Theme style changed to {Theme.CurrentThemeStyle}");
            }
        };

        Localization.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Localization.SelectedLanguage))
            {
                OnPropertyChanged(nameof(CanSetLanguageDefault));
                Logger.Log($"Language changed to {Localization.SelectedLanguage.Code}");
            }
            else if (e.PropertyName == "Item[]")
            {
                OnPropertyChanged(nameof(Localization));
            }
        };
        
        Prefs.PropertyChanged += (s, e) =>
        {
            if (!IsSettingsLocked)
            {
                SaveSettings();
                Logger.Log($"Preference changed: {e.PropertyName}");
            }
        };
        
        ConfigFilePath = ConfigService.ConfigPath;
        LoadSettings();
    }

    private void UpdateFilteredThemes()
    {
        FilteredThemeStyles = Theme.GetAvailableThemesForVariant(Theme.CurrentThemeVariant);
        if (!FilteredThemeStyles.Contains(Theme.CurrentThemeStyle))
        {
            Theme.CurrentThemeStyle = AppThemeStyle.Fluent;
        }
    }
    
    [RelayCommand]
    private void ResetAppearance()
    {
        if (IsSettingsLocked) return;

        Font.ResetSettings();
        Theme.CurrentThemeStyle = AppThemeStyle.Fluent;
        Theme.CurrentThemeVariant = AppThemeVariant.System;
        
        Localization.SelectedLanguage = Localization.AvailableLanguages.FirstOrDefault(x => x.Code == "auto") ?? Localization.AvailableLanguages[0];
        
        SaveSettings();
        Logger.Log("Reset appearance settings to default.", LogLevel.Warning);
    }

    [RelayCommand]
    private void SetVariantSystem() { if(!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.System; }
    
    [RelayCommand]
    private void SetVariantLight() { if(!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.Light; }
    
    [RelayCommand]
    private void SetVariantDark() { if(!IsSettingsLocked) Theme.CurrentThemeVariant = AppThemeVariant.Dark; }

    [RelayCommand]
    private void ToggleLockSettings()
    {
        if (IsSettingsLocked)
        {
            SaveSettings();
            Logger.Log("Settings locked.");
        }
        else
        {
            Logger.Log("Settings unlocked.");
        }
    }
    
    [RelayCommand]
    private void ToggleDeveloperMode()
    {
        SaveSettings();
        Logger.Log($"Developer Mode: {IsDeveloperModeEnabled}");
    }

    [RelayCommand]
    private void SetCurrentLanguageAsDefault()
    {
        if (IsSettingsLocked) return;
        SaveSettings();
        Logger.Log("Language saved as default.");
    }

    [RelayCommand]
    private async void CheckConfigFile()
    {
        var path = ConfigService.ConfigPath;
        if (File.Exists(path))
        {
            await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigExists"].Replace("{0}", path));
        }
        else
        {
            await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigMissing"]);
        }
    }

    [RelayCommand]
    private async Task CopyConfigPath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(ConfigFilePath);
                await MessageBoxHelper.ShowAsync(L["Msg_ConfigCheck"], L["Msg_ConfigCopied"]);
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
        SaveSettings();
        Logger.Log($"Admin Mode toggled: {IsAdminModeEnabled}");
        if (IsAdminModeEnabled)
        {
             await MessageBoxHelper.ShowAsync(L["Msg_AdminMode"], L["Msg_AdminRestart"]);
        }
    }
    
    partial void OnForceQuitTimeoutChanged(int value) => SaveSettings();
    partial void OnIsAdminModeEnabledChanged(bool value) => SaveSettings();

    public void LoadSettings()
    {
        var config = ConfigService.Load();

        IsSettingsLocked = config.IsSettingsLocked;
        IsDeveloperModeEnabled = config.IsDeveloperModeEnabled;
        
        var lang = Localization.AvailableLanguages.FirstOrDefault(x => x.Code == config.LanguageCode);
        if (lang != null) Localization.SelectedLanguage = lang;

        Theme.CurrentThemeStyle = config.ThemeStyle;
        Theme.CurrentThemeVariant = config.ThemeVariant;

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

        IsAdminModeEnabled = config.IsAdminModeEnabled;
        ForceQuitTimeout = config.ForceQuitTimeout;
        
        Logger.Log("Settings loaded from config.");
    }

    public void SaveSettings()
    {
        var config = new AppConfig
        {
            IsSettingsLocked = IsSettingsLocked,
            IsDeveloperModeEnabled = IsDeveloperModeEnabled,
            LanguageCode = Localization.SelectedLanguage.Code,
            ThemeStyle = Theme.CurrentThemeStyle,
            ThemeVariant = Theme.CurrentThemeVariant,
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

            IsAdminModeEnabled = IsAdminModeEnabled,
            ForceQuitTimeout = ForceQuitTimeout
        };

        ConfigService.Save(config);
        Logger.Log("Settings saved.");
    }
}
