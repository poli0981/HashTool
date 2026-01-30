namespace CheckHash.Models;

public class AppConfig
{
    public string LanguageCode { get; set; } = "auto";
    public AppThemeStyle ThemeStyle { get; set; } = AppThemeStyle.Fluent;
    public AppThemeVariant ThemeVariant { get; set; } = AppThemeVariant.System;
    public bool IsThemeLocked { get; set; } = false;

    public string? FontFamily { get; set; }
    public double BaseFontSize { get; set; } = 14;
    public double UiScale { get; set; } = 1.0;
    public bool IsFontLocked { get; set; } = false;
    public bool IsAutoFont { get; set; } = true;

    public bool IsHashMaskingEnabled { get; set; } = false;

    // Lock Settings
    public bool IsSettingsLocked { get; set; } = false;

    // Developer Mode
    public bool IsDeveloperModeEnabled { get; set; } = false;

    // Limit File Size
    public bool IsFileSizeLimitEnabled { get; set; } = false;
    public double FileSizeLimitValue { get; set; } = 10;
    public FileSizeUnit FileSizeLimitUnit { get; set; } = FileSizeUnit.GB;

    // Admin Mode
    public bool IsAdminModeEnabled { get; set; } = false;

    // Force Quit Timeout (App Freeze)
    public int ForceQuitTimeout { get; set; } = 5;

    // File Processing Timeout
    public bool IsFileTimeoutEnabled { get; set; } = false;
    public int FileTimeoutSeconds { get; set; } = 60;
}