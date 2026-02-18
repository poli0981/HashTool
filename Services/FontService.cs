using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
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
    private readonly string _settingsFile;
    private readonly string _logDir;
    private readonly string _logFile;

    private readonly Dictionary<string, FontFamily> _fontCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private double _baseFontSize = 14.0;

    [ObservableProperty] private bool _isAutoFont = true;

    private bool _isLoading = true;

    [ObservableProperty] private bool _isLockedFont;

    [ObservableProperty] private FontFamily _selectedFont;
    [ObservableProperty] private double _uiScale = 1.0;

    private bool _hasCheckedSettingsDir;
    private CancellationTokenSource? _saveSettingsCts;
    private CancellationTokenSource? _saveLogCts;

    public FontService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(appData, "HashTool");

        _logDir = Path.Combine(baseDir, "log", "fontlog");
        _logFile = Path.Combine(_logDir, "default_font.log");
        _settingsFile = Path.Combine(baseDir, "font_settings.json");

        _selectedFont = FontFamily.Default;
        _ = LoadSystemFontsAsync();
    }

    public static FontService Instance { get; } = new();

    public AvaloniaList<FontFamily> InstalledFonts { get; } = new();

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

        _saveLogCts?.Cancel();
        _saveLogCts = new CancellationTokenSource();
        var token = _saveLogCts.Token;

        Task.Delay(500, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            try
            {
                if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
                await File.WriteAllTextAsync(_logFile, $"Default Font Set: {fontName} at {DateTime.Now}");
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Log($"Failed to save font log: {ex.Message}", LogLevel.Error);
            }
        }, TaskScheduler.Default);
    }

    private void SaveSettings()
    {
        if (_isLoading) return;

        _saveSettingsCts?.Cancel();
        _saveSettingsCts = new CancellationTokenSource();
        var token = _saveSettingsCts.Token;

        Task.Delay(500, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            await SaveSettingsAsync(token);
        }, TaskScheduler.Default);
    }

    private async Task SaveSettingsAsync(CancellationToken token)
    {
        try
        {
            FontSettingsData? data = null;

            // Capture data on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                data = new FontSettingsData
                {
                    FontName = SelectedFont.Name,
                    UiScale = UiScale,
                    IsAutoFont = IsAutoFont,
                    IsLockedFont = IsLockedFont,
                    BaseFontSize = BaseFontSize
                };
            });

            if (token.IsCancellationRequested || data == null) return;

            var json = JsonSerializer.Serialize(data);

            if (!_hasCheckedSettingsDir)
            {
                var dir = Path.GetDirectoryName(_settingsFile);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _hasCheckedSettingsDir = true;
            }

            await File.WriteAllTextAsync(_settingsFile, json);
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to save font settings: {ex.Message}", LogLevel.Error);
        }
    }

    private async Task<FontSettingsData?> LoadSettingsDataAsync()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = await File.ReadAllTextAsync(_settingsFile);
                return JsonSerializer.Deserialize<FontSettingsData>(json);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to load font settings: {ex.Message}", LogLevel.Error);
        }

        return null;
    }

    private void ApplySettings(FontSettingsData? data)
    {
        try
        {
            if (data != null)
            {
                UiScale = data.UiScale;
                IsAutoFont = data.IsAutoFont;
                IsLockedFont = data.IsLockedFont;
                BaseFontSize = data.BaseFontSize;

                var targetFontName = data.FontName ?? FontFamily.Default.Name;

                if (!IsAutoFont && !string.IsNullOrEmpty(targetFontName))
                {
                    _fontCache.TryGetValue(targetFontName, out var existing);
                    SelectedFont = existing ?? new FontFamily(targetFontName);
                }
            }
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
            await Task.Run(async () =>
            {
                var fontManager = FontManager.Current;
                var systemFonts = fontManager.SystemFonts
                    .GroupBy(f => f.Name)
                    .Select(g => g.First())
                    .OrderBy(f => f.Name)
                    .ToList();

                // Pre-create fonts off-thread
                var newFonts = new List<FontFamily>(systemFonts.Count + 1);
                var newCache = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

                var defaultFont = FontFamily.Default;
                newFonts.Add(defaultFont);
                if (!string.IsNullOrEmpty(defaultFont.Name))
                {
                    newCache[defaultFont.Name] = defaultFont;
                }

                foreach (var font in systemFonts)
                {
                    newFonts.Add(font);
                    newCache[font.Name] = font;
                }

                var settingsData = await LoadSettingsDataAsync();

                Dispatcher.UIThread.Invoke(() =>
                {
                    InstalledFonts.Clear();
                    _fontCache.Clear();

                    InstalledFonts.AddRange(newFonts);

                    foreach (var kvp in newCache)
                    {
                        _fontCache[kvp.Key] = kvp.Value;
                    }

                    ApplySettings(settingsData);
                });
            });
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to load system fonts: {ex.Message}", LogLevel.Error);
            var settingsData = await LoadSettingsDataAsync();
            Dispatcher.UIThread.Invoke(() =>
            {
                if (InstalledFonts.Count == 0) InstalledFonts.Add(FontFamily.Default);
                ApplySettings(settingsData);
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
            if (_fontCache.TryGetValue(target, out foundFont)) break;
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