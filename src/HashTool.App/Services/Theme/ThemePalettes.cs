using System.Collections.Generic;
using Avalonia.Media;
using HashTool.Core.Configuration;

namespace HashTool.App.Services.Theme;

/// <summary>
/// Colours for each theme style. Every palette defines every key so switching styles never leaves a value from the
/// previous one behind.
/// </summary>
public static class ThemePalettes
{
    public static readonly string[] Keys =
    [
        "AppBackgroundBrush", "PaneBackgroundBrush", "GlassBorderBrush", "HighlightBrush", "StatusSuccessBrush",
        "StatusErrorBrush", "StatusWarningBrush", "StatusInfoBrush", "StatusMutedBrush", "SecondaryTextBrush"
    ];

    public static Dictionary<string, object> Get(AppThemeStyle style, bool isDark)
    {
        var p = style switch
        {
            AppThemeStyle.Google => isDark ? Make("#202124", "#303134", null) : Make("#FFFFFF", "#F1F3F4", null),
            AppThemeStyle.HighContrast => isDark
                ? Make("#000000", "#000000", "#FFFFFF", highlight: "#FFFF00", success: "#00FF00", error: "#FF5050",
                    warning: "#FFFF00", info: "#00FFFF", muted: "#FFFFFF", secondary: "#FFFFFF")
                : Make("#FFFFFF", "#FFFFFF", "#000000", highlight: "#0000CC", success: "#006400", error: "#B00000",
                    warning: "#7A4F00", info: "#00008B", muted: "#000000", secondary: "#000000"),
            // Okabe-Ito colours: distinguishable with every common form of colour blindness.
            AppThemeStyle.Colorblind => isDark
                ? Make("#121212", "#212529", null, success: "#56B4E9", error: "#E69F00", warning: "#F0E442", info: "#CC79A7")
                : Make("#F8F9FA", "#FFFFFF", null, success: "#0072B2", error: "#D55E00", warning: "#B8860B", info: "#CC79A7"),
            AppThemeStyle.Retro => isDark ? Make("#282828", "#3C3836", null) : Make("#FBF1C7", "#EBDBB2", null),
            AppThemeStyle.Glassmorphism => isDark
                ? Make("#99000000", "#991E1E1E", "#40FFFFFF")
                : Make("#99FFFFFF", "#99F3F3F3", "#40000000"),
            AppThemeStyle.Cyberpunk => isDark ? Make("#0B0C15", "#1F2235", "#00F3FF") : Make("#F0F0F5", "#FFFFFF", "#FF0099"),
            AppThemeStyle.Pastel => isDark ? Make("#2A2A35", "#3E3E4E", "#B5EAD7") : Make("#FFF0F5", "#FFFFFF", "#FFB7B2"),
            _ => isDark ? Make("#202020", "#2C2C2C", null) : Make("#F3F3F3", "#FFFFFF", null)
        };

        if (style is not (AppThemeStyle.HighContrast or AppThemeStyle.Colorblind))
        {
            p["StatusSuccessBrush"] = Brush.Parse(isDark ? "#6CCB5F" : "#0F7B0F");
            p["StatusErrorBrush"] = Brush.Parse(isDark ? "#FF99A4" : "#C42B1C");
            p["StatusWarningBrush"] = Brush.Parse(isDark ? "#FCE100" : "#9D5D00");
            p["StatusInfoBrush"] = Brush.Parse(isDark ? "#60CDFF" : "#005FB8");
        }

        p.TryAdd("StatusMutedBrush", Brush.Parse(isDark ? "#A0A0A0" : "#5F5F5F"));
        p.TryAdd("SecondaryTextBrush", Brush.Parse(isDark ? "#B8B8B8" : "#5A5A5A"));
        p.TryAdd("HighlightBrush", Brush.Parse(isDark ? "#FFD700" : "#0066CC"));
        return p;
    }

    private static Dictionary<string, object> Make(string background, string pane, string? border, string? highlight = null,
        string? success = null, string? error = null, string? warning = null, string? info = null, string? muted = null,
        string? secondary = null)
    {
        var p = new Dictionary<string, object>
        {
            ["AppBackgroundBrush"] = Brush.Parse(background),
            ["PaneBackgroundBrush"] = Brush.Parse(pane),
            ["GlassBorderBrush"] = border is null ? Brushes.Transparent : Brush.Parse(border)
        };
        if (highlight is not null) p["HighlightBrush"] = Brush.Parse(highlight);
        if (success is not null) p["StatusSuccessBrush"] = Brush.Parse(success);
        if (error is not null) p["StatusErrorBrush"] = Brush.Parse(error);
        if (warning is not null) p["StatusWarningBrush"] = Brush.Parse(warning);
        if (info is not null) p["StatusInfoBrush"] = Brush.Parse(info);
        if (muted is not null) p["StatusMutedBrush"] = Brush.Parse(muted);
        if (secondary is not null) p["SecondaryTextBrush"] = Brush.Parse(secondary);
        return p;
    }
}
