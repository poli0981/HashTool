using System.Collections.Generic;
using Avalonia.Media;
using CheckHash.Models;

namespace CheckHash.Services;

public static class ThemePalettes
{
    public static Dictionary<string, object> GetPalette(AppThemeStyle style, AppThemeVariant variant)
    {
        var p = new Dictionary<string, object>();

        var isDark = variant == AppThemeVariant.Dark;
        if (variant == AppThemeVariant.System) isDark = true;

        switch (style)
        {
            // =================================================================
            // MICA CUSTOM THEME
            // =================================================================
            case AppThemeStyle.MicaCustom:
                p["AppBackgroundColor"] = Color.Parse("#991E1E1E");
                p["AppBackgroundBrush"] = Brush.Parse("#991E1E1E");

                p["PaneBackgroundColor"] = Color.Parse("#99252525");
                p["PaneBackgroundBrush"] = Brush.Parse("#99252525");

                p["GlassBorderBrush"] = Brush.Parse("#40FFFFFF");
                break;

            // =================================================================
            // GOOGLE MATERIAL THEME
            // =================================================================
            case AppThemeStyle.Google:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["AppBackgroundBrush"] = Brush.Parse("#FFFFFF");

                    p["PaneBackgroundColor"] = Color.Parse("#F1F3F4"); // Gray 100
                    p["PaneBackgroundBrush"] = Brush.Parse("#F1F3F4");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#202124"); // Dark Gray
                    p["AppBackgroundBrush"] = Brush.Parse("#202124");

                    p["PaneBackgroundColor"] = Color.Parse("#303134"); // Gray 800
                    p["PaneBackgroundBrush"] = Brush.Parse("#303134");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }

                break;

            // =================================================================
            // HIGH CONTRAST THEME
            // =================================================================
            case AppThemeStyle.HighContrast:
                if (!isDark) // White High Contrast
                {
                    p["AppBackgroundColor"] = Colors.White;
                    p["AppBackgroundBrush"] = Brushes.White;

                    p["PaneBackgroundColor"] = Colors.White;
                    p["PaneBackgroundBrush"] = Brushes.White;
                    p["GlassBorderBrush"] = Brushes.Black;
                }
                else // Black High Contrast
                {
                    p["AppBackgroundColor"] = Colors.Black;
                    p["AppBackgroundBrush"] = Brushes.Black;

                    p["PaneBackgroundColor"] = Colors.Black;
                    p["PaneBackgroundBrush"] = Brushes.Black;
                    p["GlassBorderBrush"] = Brushes.White;
                }

                break;

            // =================================================================
            // COLORBLIND MODE (Deuteranopia Safe)
            // =================================================================
            case AppThemeStyle.Colorblind:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#F8F9FA");
                    p["AppBackgroundBrush"] = Brush.Parse("#F8F9FA");
                    p["PaneBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["PaneBackgroundBrush"] = Brush.Parse("#FFFFFF");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#121212");
                    p["AppBackgroundBrush"] = Brush.Parse("#121212");
                    p["PaneBackgroundColor"] = Color.Parse("#212529");
                    p["PaneBackgroundBrush"] = Brush.Parse("#212529");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }

                p["StatusSuccessBrush"] = Brush.Parse("#0072B2");
                p["StatusErrorBrush"] = Brush.Parse("#D55E00");
                break;

            // =================================================================
            // FLUENT (Default)
            // =================================================================
            case AppThemeStyle.Fluent:
            default:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#F8F9FA");
                    p["AppBackgroundBrush"] = Brush.Parse("#F8F9FA");
                    p["PaneBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["PaneBackgroundBrush"] = Brush.Parse("#FFFFFF");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#121212");
                    p["AppBackgroundBrush"] = Brush.Parse("#121212");
                    p["PaneBackgroundColor"] = Color.Parse("#212529");
                    p["PaneBackgroundBrush"] = Brush.Parse("#212529");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }

                break;
        }

        return p;
    }
}