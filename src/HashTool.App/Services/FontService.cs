using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Services;

/// <summary>Installed fonts, the selected UI font and size, and the per-language automatic font choice.</summary>
public sealed partial class FontService : ObservableObject
{
    private readonly ILogger<FontService> _logger;
    private readonly Dictionary<string, FontFamily> _byName = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingFontName;

    [ObservableProperty] private double _baseFontSize = 14.0;
    [ObservableProperty] private bool _isAutoFont = true;
    [ObservableProperty] private bool _isLockedFont;
    [ObservableProperty] private FontFamily _selectedFont = FontFamily.Default;
    [ObservableProperty] private double _uiScale = 1.0;

    public FontService(ILogger<FontService> logger) => _logger = logger;

    public AvaloniaList<FontFamily> InstalledFonts { get; } = [FontFamily.Default];

    /// <summary>Loads system fonts in the background (enumerating them can take a moment on Windows).</summary>
    public async Task LoadAsync()
    {
        try
        {
            var names = await Task.Run(() => FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList());
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var fonts = names.Select(n => new FontFamily(n)).ToList();
                foreach (var font in fonts) _byName[font.Name] = font;
                InstalledFonts.AddRange(fonts);
                if (_pendingFontName is { } pending) SelectFont(pending);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate system fonts");
        }
    }

    /// <summary>Selects a font by name, now or once the font list has loaded.</summary>
    public void SelectFont(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('$'))
        {
            SelectedFont = FontFamily.Default;
            _pendingFontName = null;
            return;
        }

        if (_byName.TryGetValue(name, out var font))
        {
            SelectedFont = font;
            _pendingFontName = null;
        }
        else
        {
            _pendingFontName = name;
        }
    }

    /// <summary>Name to persist, or null for the default font.</summary>
    public string? SelectedFontName => SelectedFont == FontFamily.Default ? null : SelectedFont.Name;

    partial void OnIsAutoFontChanged(bool value)
    {
        if (value) IsLockedFont = false;
    }

    partial void OnIsLockedFontChanged(bool value)
    {
        if (value) IsAutoFont = false;
    }

    /// <summary>Picks a font that renders the language's script well, when automatic font selection is on.</summary>
    public void ApplyAutomaticFont(CultureInfo culture)
    {
        if (!IsAutoFont || IsLockedFont) return;
        foreach (var candidate in CandidatesFor(culture))
        {
            if (_byName.TryGetValue(candidate, out var font))
            {
                SelectedFont = font;
                return;
            }
        }

        SelectedFont = FontFamily.Default;
    }

    public void Reset()
    {
        UiScale = 1.0;
        BaseFontSize = 14.0;
        IsLockedFont = false;
        IsAutoFont = true;
        SelectedFont = FontFamily.Default;
    }

    /// <summary>Preferred fonts per script for Windows, macOS and Linux (whichever is installed wins).</summary>
    internal static IEnumerable<string> CandidatesFor(CultureInfo culture)
    {
        var name = culture.Name;
        return culture.TwoLetterISOLanguageName switch
        {
            "ja" => ["Yu Gothic UI", "Meiryo UI", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP"],
            "ko" => ["Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR"],
            "zh" when name.Contains("Hant", StringComparison.OrdinalIgnoreCase) =>
                ["Microsoft JhengHei UI", "Microsoft JhengHei", "PingFang TC", "Noto Sans CJK TC"],
            "zh" => ["Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC"],
            "th" => ["Leelawadee UI", "Thonburi", "Noto Sans Thai", "Tahoma"],
            "lo" => ["Leelawadee UI", "Lao Sangam MN", "Noto Sans Lao"],
            "km" => ["Leelawadee UI", "Khmer Sangam MN", "Noto Sans Khmer"],
            "my" => ["Myanmar Text", "Myanmar Sangam MN", "Noto Sans Myanmar"],
            "ar" or "fa" or "ps" => ["Segoe UI", "Geeza Pro", "Noto Sans Arabic", "Tahoma"],
            "he" => ["Segoe UI", "Arial Hebrew", "Noto Sans Hebrew"],
            "hi" or "ne" => ["Nirmala UI", "Kohinoor Devanagari", "Noto Sans Devanagari"],
            "bn" => ["Nirmala UI", "Kohinoor Bangla", "Noto Sans Bengali"],
            "si" => ["Nirmala UI", "Sinhala Sangam MN", "Noto Sans Sinhala"],
            "dz" => ["Microsoft Himalaya", "Kailasa", "Noto Serif Tibetan"],
            "dv" => ["MV Boli", "Noto Sans Thaana"],
            _ => []
        };
    }
}
