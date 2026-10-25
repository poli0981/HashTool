using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HashTool.App.Infrastructure;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.App.ViewModels;
using HashTool.Core.Checksums;
using HashTool.Core.Configuration;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>Tests that change process-wide state (the UI language and culture) never run alongside other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedStateCollection
{
    public const string Name = "Shared state";
}

[Collection(SharedStateCollection.Name)]
public sealed class SettingsViewModelTests
{
    private sealed class Page : IDisposable
    {
        public Page(Func<string, bool>? answer = null)
        {
            Settings = TestServices.Settings(Path.Combine(Temp.Path, ".settings"));
            Dialogs = new RecordingDialogs { Answer = answer ?? (_ => false) };
            Font = new FontService(TestServices.Loggers.CreateLogger<FontService>());
            Vm = new SettingsViewModel(Settings, TestServices.Localization, Font, Theme, Dialogs, Clipboard, Launcher,
                AppHost.Get<UpdateService>(), LogLevel, TestServices.Loggers.CreateLogger<SettingsViewModel>());
        }

        public TempDirectory Temp { get; } = new();

        public SettingsService Settings { get; }

        public RecordingDialogs Dialogs { get; }

        public FontService Font { get; }

        public ThemeService Theme { get; } = new();

        public RecordingClipboard Clipboard { get; } = new();

        public RecordingLauncher Launcher { get; } = new();

        public LogLevelSwitch LogLevel { get; } = new();

        public SettingsViewModel Vm { get; }

        public AppSettings Current => Settings.Current;

        public void Dispose()
        {
            Settings.Dispose();
            Temp.Dispose();
        }
    }

    [AvaloniaFact]
    public void EveryOption_IsStoredInTheSettings()
    {
        using var page = new Page();
        var changed = new System.Collections.Generic.List<string?>();
        page.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.Vm.IsFileSizeLimitEnabled = true;
        page.Vm.FileSizeLimitValue = 5;
        page.Vm.FileSizeLimitUnit = FileSizeUnit.MB;
        page.Vm.IsFileTimeoutEnabled = true;
        page.Vm.FileTimeoutSeconds = 30;
        page.Vm.IsMaxFileCountEnabled = false;
        page.Vm.MaxFileCount = 50;
        page.Vm.IsMaxFolderCountEnabled = false;
        page.Vm.MaxFolderCount = 7;
        page.Vm.MaxParallelFiles = 2;
        page.Vm.OptimizeForHdd = true;
        page.Vm.IncludeHiddenFiles = true;
        page.Vm.HashLetterCase = page.Vm.HexCaseOptions.Single(o => o.Value == HexCase.Lower);
        page.Vm.DefaultExportFormat = page.Vm.ExportFormatOptions.Single(o => o.Value == ExportFormat.Json);
        page.Vm.IsHashMaskingEnabled = true;
        page.Vm.ShowReadWriteSpeed = true;
        page.Vm.CheckForUpdatesOnStartup = false;

        var s = page.Current;
        Assert.True(s.IsFileSizeLimitEnabled);
        Assert.Equal(5, s.FileSizeLimitValue);
        Assert.Equal(FileSizeUnit.MB, page.Vm.FileSizeLimitUnit);
        Assert.True(page.Vm.IsFileTimeoutEnabled);
        Assert.Equal(30, page.Vm.FileTimeoutSeconds);
        Assert.False(page.Vm.IsMaxFileCountEnabled);
        Assert.Equal(50, page.Vm.MaxFileCount);
        Assert.False(page.Vm.IsMaxFolderCountEnabled);
        Assert.Equal(7, page.Vm.MaxFolderCount);
        Assert.Equal(2, page.Vm.MaxParallelFiles);
        Assert.True(page.Vm.OptimizeForHdd);
        Assert.True(page.Vm.IncludeHiddenFiles);
        Assert.Equal(HexCase.Lower, page.Vm.HashLetterCase.Value);
        Assert.Equal(ExportFormat.Json, page.Vm.DefaultExportFormat.Value);
        Assert.True(page.Vm.IsHashMaskingEnabled);
        Assert.True(page.Vm.ShowReadWriteSpeed);
        Assert.False(page.Vm.CheckForUpdatesOnStartup);
        Assert.Contains(nameof(SettingsViewModel.MaxFolderCount), changed);
        Assert.Contains(nameof(SettingsViewModel.CheckForUpdatesOnStartup), changed);
    }

    [AvaloniaFact]
    public void DeveloperMode_LowersTheLogLevel()
    {
        using var page = new Page();

        page.Vm.IsDeveloperModeEnabled = true;
        Assert.Equal(LogLevel.Debug, page.LogLevel.Minimum);
        Assert.True(page.Vm.IsDeveloperModeEnabled);

        page.Vm.IsDeveloperModeEnabled = false;
        Assert.Equal(LogLevel.Information, page.LogLevel.Minimum);
    }

    [AvaloniaFact]
    public async Task LockingTheSettings_DisablesEditing_AndReset()
    {
        using var page = new Page(_ => true);

        page.Vm.IsSettingsLocked = true;

        Assert.True(page.Vm.IsSettingsLocked);
        Assert.False(page.Vm.CanEdit);
        Assert.False(page.Vm.CanChangeTheme);
        Assert.False(page.Vm.CanChangeFont);
        page.Vm.SetVariantCommand.Execute(AppThemeVariant.Dark);
        Assert.Equal(AppThemeVariant.System, page.Theme.CurrentThemeVariant);
        await page.Vm.ResetSettingsCommand.ExecuteAsync(null);
        Assert.Empty(page.Dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task Reset_RestoresDefaults_OnlyWhenConfirmed()
    {
        var answer = false;
        using var page = new Page(_ => answer);
        page.Vm.MaxFileCount = 5;
        page.Vm.IsDeveloperModeEnabled = true;

        await page.Vm.ResetSettingsCommand.ExecuteAsync(null);
        Assert.Equal(5, page.Current.MaxFileCount);

        answer = true;
        await page.Vm.ResetSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new AppSettings().MaxFileCount, page.Current.MaxFileCount);
        Assert.False(page.Current.IsDeveloperModeEnabled);
        Assert.Equal(LogLevel.Information, page.LogLevel.Minimum);
        Assert.Equal(2, page.Dialogs.Confirmations.Count);
    }

    [AvaloniaFact]
    public void ThemeAndFontChanges_AreSaved()
    {
        using var page = new Page();

        page.Vm.SetVariantCommand.Execute(AppThemeVariant.Dark);
        page.Theme.CurrentThemeStyle = AppThemeStyle.Pastel;
        page.Theme.IsThemeLocked = true;
        page.Vm.SetVariantCommand.Execute(AppThemeVariant.Light);

        Assert.Equal(AppThemeVariant.Dark, page.Current.ThemeVariant);
        Assert.Equal(AppThemeStyle.Pastel, page.Current.ThemeStyle);
        Assert.True(page.Current.IsThemeLocked);
        Assert.False(page.Vm.CanChangeTheme);

        page.Font.BaseFontSize = 16;
        page.Font.UiScale = 1.25;
        page.Font.IsLockedFont = true;
        Assert.False(page.Font.IsAutoFont);
        page.Font.SelectedFont = new FontFamily("Inter");
        page.Font.IsAutoFont = true;

        Assert.Equal(16, page.Current.BaseFontSize);
        Assert.Equal(1.25, page.Current.UiScale);
        Assert.True(page.Current.IsAutoFont);
        Assert.False(page.Current.IsFontLocked);
        Assert.True(page.Vm.CanChangeFont);
    }

    [AvaloniaFact]
    public void Apply_PushesTheStoredSettingsIntoTheServices()
    {
        using var page = new Page();
        var language = TestServices.Localization.SelectedLanguage;
        try
        {
            page.Settings.Update(s =>
            {
                s.LanguageCode = "de-DE";
                s.ThemeStyle = AppThemeStyle.Retro;
                s.ThemeVariant = AppThemeVariant.Light;
                s.IsAutoFont = false;
                s.FontFamily = "Some Font";
                s.BaseFontSize = 13;
                s.IsDeveloperModeEnabled = true;
            });

            page.Vm.Apply();

            Assert.Equal("de-DE", TestServices.Localization.SelectedLanguage.Code);
            Assert.Equal(AppThemeStyle.Retro, page.Theme.CurrentThemeStyle);
            Assert.Equal(AppThemeVariant.Light, page.Theme.CurrentThemeVariant);
            Assert.Equal(13, page.Font.BaseFontSize);
            Assert.Equal(LogLevel.Debug, page.LogLevel.Minimum);
            // The settings are not written back while they are being applied.
            Assert.Equal("de-DE", page.Current.LanguageCode);
        }
        finally
        {
            TestServices.Localization.SelectedLanguage = language;
        }
    }

    [AvaloniaFact]
    public void SelectedLanguage_ChangesTheUiAndIsSaved()
    {
        using var page = new Page();
        var language = TestServices.Localization.SelectedLanguage;
        var proxy = page.Vm.Localization;
        try
        {
            var japanese = TestServices.Localization.Find("ja-JP");
            page.Vm.SelectedLanguage = japanese;
            page.Vm.SelectedLanguage = japanese;
            page.Vm.SelectedLanguage = null!;

            Assert.Equal("ja-JP", page.Current.LanguageCode);
            Assert.Equal("ja-JP", page.Vm.SelectedLanguage.Code);
            Assert.NotSame(proxy, page.Vm.Localization);
            Assert.Contains(page.Vm.AutomaticParallelism.ToString(CultureInfo.InvariantCulture), page.Vm.ParallelFilesHint, StringComparison.Ordinal);
        }
        finally
        {
            TestServices.Localization.SelectedLanguage = language;
        }
    }

    [AvaloniaFact]
    public async Task ConfigurationCommands_CopyThePathAndOpenTheDataFolder()
    {
        using var page = new Page();

        await page.Vm.CopyConfigPathCommand.ExecuteAsync(null);
        await page.Vm.OpenDataFolderCommand.ExecuteAsync(null);

        Assert.Equal([page.Settings.FilePath], page.Clipboard.Texts);
        Assert.Equal(page.Settings.FilePath, page.Vm.ConfigFilePath);
        Assert.Equal(DialogIcon.Success, page.Dialogs.Shown.Single().Icon);
        Assert.Equal([AppPaths.DataDirectory], page.Launcher.Folders);
    }

    [AvaloniaFact]
    public void SystemIntegration_ReflectsThePlatform()
    {
        using var page = new Page();

        Assert.Equal(OperatingSystem.IsWindows(), page.Vm.IsWindows);
        Assert.Equal(Elevation.IsElevated, page.Vm.IsElevated);
        Assert.Equal(Elevation.CanRestartElevated, page.Vm.CanRestartElevated);
        if (!OperatingSystem.IsWindows()) Assert.False(page.Vm.CanUseShellIntegration);
        Assert.NotEmpty(page.Vm.ThemeStyles);
        Assert.NotEmpty(page.Vm.FileSizeUnits);

        // Setting the current value again does nothing (and never touches the registry).
        page.Vm.IsShellIntegrationEnabled = page.Vm.IsShellIntegrationEnabled;
        page.Vm.RefreshShellIntegration();
        // Elsewhere than Windows the Explorer integration is a no-op, so the switch itself can be exercised.
        if (OperatingSystem.IsWindows()) return;
        page.Vm.IsShellIntegrationEnabled = true;
        Assert.True(page.Current.IsShellIntegrationEnabled);
        page.Vm.RefreshShellIntegration();
        page.Vm.IsShellIntegrationEnabled = false;
        Assert.False(page.Current.IsShellIntegrationEnabled);
    }

    [AvaloniaFact]
    public void RestartElevated_DoesNothingWhereElevationIsUnavailable()
    {
        // On a non-administrator Windows account this would show a UAC prompt: only run it where nothing can start.
        if (Elevation.CanRestartElevated) return;
        using var page = new Page();
        var shutdown = false;
        page.Vm.RequestShutdown += (_, _) => shutdown = true;

        page.Vm.RestartElevatedCommand.Execute(null);

        Assert.False(shutdown);
    }
}

public sealed class FontServiceTests
{
    private static FontService Create() => new(TestServices.Loggers.CreateLogger<FontService>());

    [AvaloniaFact]
    public async Task SelectFont_WaitsForTheFontListToLoad()
    {
        var fonts = Create();
        fonts.SelectFont("Inter");
        Assert.Equal(FontFamily.Default, fonts.SelectedFont);

        await fonts.LoadAsync();

        Assert.Contains(fonts.InstalledFonts, f => f == FontFamily.Default);
        if (fonts.InstalledFonts.Any(f => f.Name == "Inter")) Assert.Equal("Inter", fonts.SelectedFontName);
        fonts.SelectFont("$Default");
        Assert.Null(fonts.SelectedFontName);
        fonts.SelectFont(null);
        Assert.Equal(FontFamily.Default, fonts.SelectedFont);
    }

    [AvaloniaFact]
    public void AutomaticAndLockedFont_ExcludeEachOther()
    {
        var fonts = Create();

        fonts.IsLockedFont = true;
        Assert.False(fonts.IsAutoFont);
        fonts.ApplyAutomaticFont(CultureInfo.GetCultureInfo("ja-JP"));
        Assert.Equal(FontFamily.Default, fonts.SelectedFont);

        fonts.IsAutoFont = true;
        Assert.False(fonts.IsLockedFont);
        fonts.ApplyAutomaticFont(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(FontFamily.Default, fonts.SelectedFont);

        fonts.UiScale = 2;
        fonts.BaseFontSize = 20;
        fonts.Reset();
        Assert.Equal(1.0, fonts.UiScale);
        Assert.Equal(14.0, fonts.BaseFontSize);
        Assert.True(fonts.IsAutoFont);
    }

    [Theory]
    [InlineData("ja-JP", "Yu Gothic UI")]
    [InlineData("ko-KR", "Malgun Gothic")]
    [InlineData("zh-Hant", "Microsoft JhengHei UI")]
    [InlineData("zh-Hans-CN", "Microsoft YaHei UI")]
    [InlineData("th-TH", "Leelawadee UI")]
    [InlineData("lo-LA", "Lao Sangam MN")]
    [InlineData("km-KH", "Khmer Sangam MN")]
    [InlineData("my-MM", "Myanmar Text")]
    [InlineData("ar", "Geeza Pro")]
    [InlineData("fa-IR", "Noto Sans Arabic")]
    [InlineData("ps-AF", "Tahoma")]
    [InlineData("he-IL", "Arial Hebrew")]
    [InlineData("hi-IN", "Nirmala UI")]
    [InlineData("ne-NP", "Kohinoor Devanagari")]
    [InlineData("bn-BD", "Kohinoor Bangla")]
    [InlineData("si-LK", "Sinhala Sangam MN")]
    [InlineData("dz-BT", "Microsoft Himalaya")]
    [InlineData("dv-MV", "MV Boli")]
    public void EachScript_HasPreferredFonts(string culture, string expected)
    {
        Assert.Contains(expected, FontService.CandidatesFor(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void LatinScripts_UseTheDefaultFont()
    {
        Assert.Empty(FontService.CandidatesFor(CultureInfo.GetCultureInfo("vi-VN")));
    }
}
