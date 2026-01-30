using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public class FontSettingsData
{
    public string? FontName { get; set; }
    public double UiScale { get; set; } = 1.0;
    public bool IsAutoFont { get; set; } = true;
    public bool IsLockedFont { get; set; }
    public double BaseFontSize { get; set; } = 14.0;
}

public partial class FontService : ObservableObject
{
    private const string SettingsFile = "font_settings.json";
    private const string LogDir = "log/fontlog";
    private const string LogFile = "log/fontlog/default_font.log";

    [ObservableProperty] private double _baseFontSize = 14.0;

    [ObservableProperty] private bool _isAutoFont = true;

    private bool _isLoading = true;

    [ObservableProperty] private bool _isLockedFont;

    [ObservableProperty] private FontFamily _selectedFont;
    [ObservableProperty] private double _uiScale = 1.0;

    public FontService()
    {
        _selectedFont = FontFamily.Default;
        _ = LoadSystemFontsAsync();
    }

    public static FontService Instance { get; } = new();

    public ObservableCollection<FontFamily> InstalledFonts { get; } = new();

    partial void OnSelectedFontChanged(FontFamily value)
    {
        if (IsLockedFont) SaveLog(value.Name);
        SaveSettings();
    }

    partial void OnUiScaleChanged(double value)
    {
        SaveSettings();
    }

    partial void OnIsAutoFontChanged(bool value)
    {
        if (value)
        {
            IsLockedFont = false; // Disable locked font if auto font is enabled
            var currentLang = LocalizationService.Instance.SelectedLanguage.Code;
            SetFontForLanguage(currentLang);
        }

        SaveSettings();
    }

    partial void OnIsLockedFontChanged(bool value)
    {
        if (value)
        {
            IsAutoFont = false;
            SaveLog(SelectedFont.Name);
        }

        SaveSettings();
    }

    partial void OnBaseFontSizeChanged(double value)
    {
        SaveSettings();
    }

    private void SaveLog(string fontName)
    {
        if (_isLoading) return;
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogFile, $"Default Font Set: {fontName} at {DateTime.Now}");
        }
        catch
        {
        }
    }

    private void SaveSettings()
    {
        if (_isLoading) return;

        try
        {
            var data = new FontSettingsData
            {
                FontName = SelectedFont.Name,
                UiScale = UiScale,
                IsAutoFont = IsAutoFont,
                IsLockedFont = IsLockedFont,
                BaseFontSize = BaseFontSize
            };

            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var data = JsonSerializer.Deserialize<FontSettingsData>(json);

                if (data != null)
                {
                    UiScale = data.UiScale;
                    IsAutoFont = data.IsAutoFont;
                    IsLockedFont = data.IsLockedFont;
                    BaseFontSize = data.BaseFontSize;

                    var targetFontName = data.FontName ?? FontFamily.Default.Name;

                    if (!IsAutoFont && !string.IsNullOrEmpty(targetFontName))
                    {
                        var existing = InstalledFonts.FirstOrDefault(f => f.Name == targetFontName);
                        SelectedFont = existing ?? new FontFamily(targetFontName);
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadSystemFontsAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var fontManager = FontManager.Current;
                var fontNames = fontManager.SystemFonts
                    .Select(f => f.Name)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                Dispatcher.UIThread.Invoke(() =>
                {
                    InstalledFonts.Clear();
                    InstalledFonts.Add(FontFamily.Default);
                    foreach (var name in fontNames) InstalledFonts.Add(new FontFamily(name));
                    LoadSettings();
                });
            });
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                if (InstalledFonts.Count == 0) InstalledFonts.Add(FontFamily.Default);
                LoadSettings();
            });
        }
    }

    public void SetFontForLanguage(string langCode)
    {
        if (!IsAutoFont && !IsLockedFont) return;
        if (IsLockedFont) return;

        string[] targetFonts;

        if (langCode.StartsWith("ja"))
            targetFonts = new[] { "Meiryo UI", "Yu Gothic UI", "MS UI Gothic" };
        else if (langCode.StartsWith("ko"))
            targetFonts = new[] { "Malgun Gothic", "Batang" };
        else if (langCode.StartsWith("ar") || langCode.StartsWith("fa"))
            targetFonts = new[] { "Segoe UI", "Arial", "Tahoma" };
        else
            targetFonts = new[] { "Inter", "Segoe UI", "Arial", "Roboto" };

        FontFamily? foundFont = null;
        foreach (var target in targetFonts)
        {
            foundFont = InstalledFonts.FirstOrDefault(f => f.Name.Contains(target, StringComparison.OrdinalIgnoreCase));
            if (foundFont != null) break;
        }

        if (foundFont != null) SelectedFont = foundFont;
        else SelectedFont = FontFamily.Default;
    }

    // 
    public void ResetSettings()
    {
        UiScale = 1.0;
        BaseFontSize = 14.0;
        SelectedFont = FontFamily.Default;
        IsAutoFont = true;
        IsLockedFont = false;
        SaveSettings();
    }
}