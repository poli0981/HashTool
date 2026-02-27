using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class FontService : ObservableObject
{
    private readonly Dictionary<string, FontFamily> _fontCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _targetFontName;

    [ObservableProperty] private double _baseFontSize = 14.0;

    [ObservableProperty] private bool _isAutoFont = true;

    [ObservableProperty] private bool _isLockedFont;

    [ObservableProperty] private FontFamily _selectedFont;
    [ObservableProperty] private double _uiScale = 1.0;

    private FontService()
    {
        _selectedFont = FontFamily.Default;
        _ = LoadSystemFontsAsync();
    }

    public static FontService Instance { get; } = new();

    public AvaloniaList<FontFamily> InstalledFonts { get; } = new();

    partial void OnSelectedFontChanged(FontFamily value)
    {
        if (IsLockedFont) LoggerService.Instance.Log($"Default Font Set: {value.Name}");
    }


    partial void OnIsAutoFontChanged(bool value)
    {
        if (value)
        {
            IsLockedFont = false; // Disable locked font if auto font is enabled
            var currentLang = LocalizationService.Instance.SelectedLanguage.Code;
            SetFontForLanguage(currentLang);
        }
    }

    partial void OnIsLockedFontChanged(bool value)
    {
        if (value)
        {
            IsAutoFont = false;
            LoggerService.Instance.Log($"Default Font Set: {SelectedFont.Name}");
        }
    }


    public void SetTargetFont(string fontName)
    {
        _targetFontName = fontName;
        var font = InstalledFonts.FirstOrDefault(x => x.Name == fontName);
        if (font != null)
        {
            SelectedFont = font;
        }
    }

    private async Task LoadSystemFontsAsync()
    {
        try
        {
            await Task.Run(() =>
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

                Dispatcher.UIThread.Invoke(() =>
                {
                    InstalledFonts.Clear();
                    _fontCache.Clear();

                    InstalledFonts.AddRange(newFonts);

                    foreach (var kvp in newCache)
                    {
                        _fontCache[kvp.Key] = kvp.Value;
                    }

                    if (!string.IsNullOrEmpty(_targetFontName))
                    {
                        var font = InstalledFonts.FirstOrDefault(x => x.Name == _targetFontName);
                        if (font != null) SelectedFont = font;
                    }
                });
            });
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to load system fonts: {ex.Message}", LogLevel.Error);
            Dispatcher.UIThread.Invoke(() =>
            {
                if (InstalledFonts.Count == 0) InstalledFonts.Add(FontFamily.Default);
            });
        }
    }

    public void SetFontForLanguage(string langCode)
    {
        if (!IsAutoFont && !IsLockedFont) return;
        if (IsLockedFont) return;

        string[] targetFonts;

        if (langCode.StartsWith("ja"))
            targetFonts = ["Meiryo UI", "Yu Gothic UI", "MS UI Gothic"];
        else if (langCode.StartsWith("ko"))
            targetFonts = ["Malgun Gothic", "Batang"];
        else if (langCode.StartsWith("ar") || langCode.StartsWith("fa"))
            targetFonts = ["Segoe UI", "Arial", "Tahoma"];
        else
            targetFonts = ["Inter", "Segoe UI", "Arial", "Roboto"];

        FontFamily? foundFont = null;
        foreach (var target in targetFonts)
        {
            if (_fontCache.TryGetValue(target, out foundFont)) break;
        }

        if (foundFont != null) SelectedFont = foundFont;
        else SelectedFont = FontFamily.Default;
    }

    public void ResetSettings()
    {
        UiScale = 1.0;
        BaseFontSize = 14.0;
        SelectedFont = FontFamily.Default;
        IsAutoFont = true;
        IsLockedFont = false;
    }
}