using System;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;

namespace HashTool.Core.Configuration;

/// <summary>Saved window size and position.</summary>
public sealed class WindowPlacement
{
    public double Width { get; set; }
    public double Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool IsMaximized { get; set; }
}

/// <summary>
/// Everything persisted in <c>config.json</c>. Property names match Hash Tool 1.x so existing files keep loading; unknown
/// properties (such as the removed <c>IsAdminModeEnabled</c>) are ignored.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Language & appearance
    public string LanguageCode { get; set; } = "auto";
    public bool ShowLanguageChangeWarning { get; set; } = true;
    public AppThemeStyle ThemeStyle { get; set; } = AppThemeStyle.Fluent;
    public AppThemeVariant ThemeVariant { get; set; } = AppThemeVariant.System;
    public bool IsThemeLocked { get; set; }
    public string? FontFamily { get; set; }
    public double BaseFontSize { get; set; } = 14;
    public double UiScale { get; set; } = 1.0;
    public bool IsFontLocked { get; set; }
    public bool IsAutoFont { get; set; } = true;
    public WindowPlacement? Window { get; set; }

    // Behaviour
    public bool IsHashMaskingEnabled { get; set; }
    public bool IsSettingsLocked { get; set; }
    public bool IsDeveloperModeEnabled { get; set; }
    public bool ShowReadWriteSpeed { get; set; }
    public HexCase HashLetterCase { get; set; } = HexCase.Upper;
    public ExportFormat DefaultExportFormat { get; set; } = ExportFormat.Gnu;
    public HashType CreateAlgorithm { get; set; } = HashType.SHA256;
    public HashType VerifyAlgorithm { get; set; } = HashType.SHA256;

    // Limits
    public bool IsFileSizeLimitEnabled { get; set; }
    public double FileSizeLimitValue { get; set; } = 10;
    public FileSizeUnit FileSizeLimitUnit { get; set; } = FileSizeUnit.GB;
    public bool IsFileTimeoutEnabled { get; set; }
    public int FileTimeoutSeconds { get; set; } = 60;
    public bool IsMaxFileCountEnabled { get; set; } = true;
    public int MaxFileCount { get; set; } = 1000;
    public bool IsMaxFolderCountEnabled { get; set; } = true;
    public int MaxFolderCount { get; set; } = 3;

    // Performance
    /// <summary>Files hashed in parallel; 0 = automatic.</summary>
    public int MaxParallelFiles { get; set; }
    public bool OptimizeForHdd { get; set; }
    public bool IncludeHiddenFiles { get; set; }

    // Updates & integration
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public bool IsShellIntegrationEnabled { get; set; }

    /// <summary>Maximum file size in bytes, or <see cref="long.MaxValue"/> when the limit is off.</summary>
    public long MaxFileSizeBytes()
    {
        if (!IsFileSizeLimitEnabled) return long.MaxValue;
        double multiplier = FileSizeLimitUnit switch
        {
            FileSizeUnit.KB => ByteSize.OneKB,
            FileSizeUnit.MB => ByteSize.OneMB,
            FileSizeUnit.GB => ByteSize.OneGB,
            FileSizeUnit.TB => ByteSize.OneTB,
            FileSizeUnit.PB => ByteSize.OnePB,
            _ => 1
        };
        var bytes = FileSizeLimitValue * multiplier;
        return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Max(1, bytes);
    }

    /// <summary>Clamps every value into the range the UI allows, so a hand-edited or corrupted file cannot break the app.</summary>
    public AppSettings Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        if (string.IsNullOrWhiteSpace(LanguageCode)) LanguageCode = "auto";
        ThemeStyle = Enum.IsDefined(ThemeStyle) ? ThemeStyle : AppThemeStyle.Fluent;
        ThemeVariant = Enum.IsDefined(ThemeVariant) ? ThemeVariant : AppThemeVariant.System;
        BaseFontSize = Clamp(BaseFontSize, 10, 32, 14);
        UiScale = Clamp(UiScale, 0.8, 2.5, 1.0);
        HashLetterCase = Enum.IsDefined(HashLetterCase) ? HashLetterCase : HexCase.Upper;
        DefaultExportFormat = Enum.IsDefined(DefaultExportFormat) ? DefaultExportFormat : ExportFormat.Gnu;
        CreateAlgorithm = Enum.IsDefined(CreateAlgorithm) ? CreateAlgorithm : HashType.SHA256;
        VerifyAlgorithm = Enum.IsDefined(VerifyAlgorithm) ? VerifyAlgorithm : HashType.SHA256;
        FileSizeLimitValue = Clamp(FileSizeLimitValue, 1, 1_000_000, 10);
        FileSizeLimitUnit = Enum.IsDefined(FileSizeLimitUnit) ? FileSizeLimitUnit : FileSizeUnit.GB;
        FileTimeoutSeconds = Math.Clamp(FileTimeoutSeconds, 10, 3600);
        MaxFileCount = Math.Clamp(MaxFileCount, 1, 1_000_000);
        MaxFolderCount = Math.Clamp(MaxFolderCount, 1, 100);
        MaxParallelFiles = Math.Clamp(MaxParallelFiles, 0, 64);
        if (Window is { } w && (!double.IsFinite(w.Width) || !double.IsFinite(w.Height) || w.Width < 200 || w.Height < 150))
            Window = null;
        return this;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
