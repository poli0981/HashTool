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
            // RETRO THEME
            // =================================================================
            case AppThemeStyle.Retro:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#FBF1C7");
                    p["AppBackgroundBrush"] = Brush.Parse("#FBF1C7");
                    p["PaneBackgroundColor"] = Color.Parse("#EBDBB2");
                    p["PaneBackgroundBrush"] = Brush.Parse("#EBDBB2");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#282828");
                    p["AppBackgroundBrush"] = Brush.Parse("#282828");
                    p["PaneBackgroundColor"] = Color.Parse("#3C3836");
                    p["PaneBackgroundBrush"] = Brush.Parse("#3C3836");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                break;

            // =================================================================
            // GLASSMORPHISM THEME
            // =================================================================
            case AppThemeStyle.Glassmorphism:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#99FFFFFF");
                    p["AppBackgroundBrush"] = Brush.Parse("#99FFFFFF");
                    p["PaneBackgroundColor"] = Color.Parse("#99F3F3F3");
                    p["PaneBackgroundBrush"] = Brush.Parse("#99F3F3F3");
                    p["GlassBorderBrush"] = Brush.Parse("#40000000");
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#99000000");
                    p["AppBackgroundBrush"] = Brush.Parse("#99000000");
                    p["PaneBackgroundColor"] = Color.Parse("#991E1E1E");
                    p["PaneBackgroundBrush"] = Brush.Parse("#991E1E1E");
                    p["GlassBorderBrush"] = Brush.Parse("#40FFFFFF");
                }
                break;

            // =================================================================
            // CYBERPUNK THEME
            // =================================================================
            case AppThemeStyle.Cyberpunk:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#F0F0F5");
                    p["AppBackgroundBrush"] = Brush.Parse("#F0F0F5");
                    p["PaneBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["PaneBackgroundBrush"] = Brush.Parse("#FFFFFF");
                    p["GlassBorderBrush"] = Brush.Parse("#FF0099");
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#0B0C15");
                    p["AppBackgroundBrush"] = Brush.Parse("#0B0C15");
                    p["PaneBackgroundColor"] = Color.Parse("#1F2235");
                    p["PaneBackgroundBrush"] = Brush.Parse("#1F2235");
                    p["GlassBorderBrush"] = Brush.Parse("#00F3FF");
                }
                break;

            // =================================================================
            // PASTEL THEME
            // =================================================================
            case AppThemeStyle.Pastel:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#FFF0F5");
                    p["AppBackgroundBrush"] = Brush.Parse("#FFF0F5");
                    p["PaneBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["PaneBackgroundBrush"] = Brush.Parse("#FFFFFF");
                    p["GlassBorderBrush"] = Brush.Parse("#FFB7B2");
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#2A2A35");
                    p["AppBackgroundBrush"] = Brush.Parse("#2A2A35");
                    p["PaneBackgroundColor"] = Color.Parse("#3E3E4E");
                    p["PaneBackgroundBrush"] = Brush.Parse("#3E3E4E");
                    p["GlassBorderBrush"] = Brush.Parse("#B5EAD7");
                }
                break;

            // =================================================================
            // FLUENT (Default)
            // =================================================================
            case AppThemeStyle.Fluent:
            default:
                if (!isDark)
                {
                    p["AppBackgroundColor"] = Color.Parse("#F3F3F3");
                    p["AppBackgroundBrush"] = Brush.Parse("#F3F3F3");
                    p["PaneBackgroundColor"] = Color.Parse("#FFFFFF");
                    p["PaneBackgroundBrush"] = Brush.Parse("#FFFFFF");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }
                else
                {
                    p["AppBackgroundColor"] = Color.Parse("#202020");
                    p["AppBackgroundBrush"] = Brush.Parse("#202020");
                    p["PaneBackgroundColor"] = Color.Parse("#2C2C2C");
                    p["PaneBackgroundBrush"] = Brush.Parse("#2C2C2C");
                    p["GlassBorderBrush"] = Brushes.Transparent;
                }

                break;
        }

        return p;
    }
}