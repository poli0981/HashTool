using System;
using System.IO;
using System.Threading.Tasks;
using HashTool.Core.Configuration;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Configuration;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SettingsStore Store() => new(Path.Combine(_temp.Path, "settings"));

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        Assert.Equal(SettingsLoadResult.CreatedDefaults, Store().Load(out var settings));
        Assert.Equal("auto", settings.LanguageCode);
        Assert.True(settings.CheckForUpdatesOnStartup);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips_AndLeavesNoTempFile()
    {
        var store = Store();
        var settings = new AppSettings
        {
            LanguageCode = "vi-VN",
            ThemeVariant = AppThemeVariant.Dark,
            HashLetterCase = HexCase.Lower,
            VerifyAlgorithm = HashType.BLAKE3,
            Window = new WindowPlacement { Width = 1200, Height = 800, X = 10, Y = 20 }
        };

        await store.SaveAsync(settings, TestContext.Current.CancellationToken);
        Assert.Equal(SettingsLoadResult.Loaded, store.Load(out var loaded));

        Assert.Equal(("vi-VN", AppThemeVariant.Dark, HexCase.Lower, HashType.BLAKE3), (loaded.LanguageCode, loaded.ThemeVariant, loaded.HashLetterCase, loaded.VerifyAlgorithm));
        Assert.Equal(1200, loaded.Window!.Width);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
        Assert.Contains("\"ThemeVariant\": \"Dark\"", await File.ReadAllTextAsync(store.FilePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LoadsVersion1Files_WithNumericEnumsAndRemovedSettings()
    {
        var store = Store();
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, """
            {
              "LanguageCode": "tr-tr",
              "ThemeStyle": 3,
              "ThemeVariant": 2,
              "IsAdminModeEnabled": true,
              "ForceQuitTimeout": 5,
              "FileSizeLimitUnit": 4,
              "UiScale": 9.5,
              "MaxFileCount": -3
            }
            """);

        Assert.Equal(SettingsLoadResult.Loaded, store.Load(out var settings));

        Assert.Equal(AppThemeStyle.Colorblind, settings.ThemeStyle);
        Assert.Equal(AppThemeVariant.Dark, settings.ThemeVariant);
        Assert.Equal(FileSizeUnit.TB, settings.FileSizeLimitUnit);
        Assert.Equal(2.5, settings.UiScale);
        Assert.Equal(1, settings.MaxFileCount);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void CorruptFile_IsBackedUp_AndDefaultsAreUsed()
    {
        var store = Store();
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, "{ \"LanguageCode\": ");

        Assert.Equal(SettingsLoadResult.RecoveredFromCorruption, store.Load(out var settings));

        Assert.Equal("auto", settings.LanguageCode);
        Assert.False(File.Exists(store.FilePath));
        Assert.Single(Directory.GetFiles(store.Directory, "config.json.bak-*"));
    }

    [Fact]
    public void MigratesFontSettingsFrom1x_AndRemovesTheOldFiles()
    {
        var store = Store();
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, "{ \"LanguageCode\": \"de-DE\", \"BaseFontSize\": 14 }");
        File.WriteAllText(store.LegacyFontSettingsPath,
            "{\"FontName\":\"Segoe UI\",\"UiScale\":1.3,\"IsAutoFont\":false,\"IsLockedFont\":true,\"BaseFontSize\":16}");
        var fontLog = Directory.CreateDirectory(Path.Combine(_temp.Path, "log", "fontlog")).FullName;
        File.WriteAllText(Path.Combine(fontLog, "default_font.log"), "x");

        store.Load(out var settings);

        Assert.Equal(("de-DE", "Segoe UI", 1.3, 16.0, false, true),
            (settings.LanguageCode, settings.FontFamily, settings.UiScale, settings.BaseFontSize, settings.IsAutoFont, settings.IsFontLocked));
        Assert.False(File.Exists(store.LegacyFontSettingsPath));
        Assert.False(Directory.Exists(fontLog));
        store.Load(out var reloaded);
        Assert.Equal("Segoe UI", reloaded.FontFamily);
    }

    [Fact]
    public void MaxFileSizeBytes()
    {
        Assert.Equal(long.MaxValue, new AppSettings().MaxFileSizeBytes());
        Assert.Equal(10L * ByteSize.OneGB, new AppSettings { IsFileSizeLimitEnabled = true }.MaxFileSizeBytes());
        Assert.Equal(long.MaxValue, new AppSettings { IsFileSizeLimitEnabled = true, FileSizeLimitValue = 1e9, FileSizeLimitUnit = FileSizeUnit.PB }.MaxFileSizeBytes());
    }

    [Fact]
    public void Normalize_FixesInvalidValues()
    {
        var settings = new AppSettings
        {
            LanguageCode = " ",
            ThemeStyle = (AppThemeStyle)99,
            BaseFontSize = double.NaN,
            FileTimeoutSeconds = 0,
            Window = new WindowPlacement { Width = 10, Height = 10 }
        }.Normalize();

        Assert.Equal(("auto", AppThemeStyle.Fluent, 14.0, 10), (settings.LanguageCode, settings.ThemeStyle, settings.BaseFontSize, settings.FileTimeoutSeconds));
        Assert.Null(settings.Window);
    }
}
