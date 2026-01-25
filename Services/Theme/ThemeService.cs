using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using CheckHash.Models;
using CheckHash.Services.ThemeEffects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class ThemeService : ObservableObject
{
    public static ThemeService Instance { get; } = new();

    [ObservableProperty] private AppThemeStyle _currentThemeStyle = AppThemeStyle.Fluent;
    [ObservableProperty] private AppThemeVariant _currentThemeVariant = AppThemeVariant.System;
    
    public ThemeService()
    {
        if (Application.Current?.PlatformSettings != null)
        {
            Application.Current.PlatformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (CurrentThemeVariant == AppThemeVariant.System)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme());
        }
    }

    partial void OnCurrentThemeStyleChanged(AppThemeStyle value) => ApplyTheme();
    partial void OnCurrentThemeVariantChanged(AppThemeVariant value) => ApplyTheme();

    public void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null) return;

        // 1. Apply Theme Variant
        var requestedVariant = CurrentThemeVariant switch
        {
            AppThemeVariant.Light => Avalonia.Styling.ThemeVariant.Light,
            AppThemeVariant.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
        
        if (app.RequestedThemeVariant != requestedVariant)
        {
            app.RequestedThemeVariant = requestedVariant;
        }

        // 2. Determine effective variant for palette logic
        AppThemeVariant effectiveVariant = CurrentThemeVariant;
        if (CurrentThemeVariant == AppThemeVariant.System)
        {
            var systemVariant = app.PlatformSettings?.GetColorValues().ThemeVariant;
            if (systemVariant == PlatformThemeVariant.Light)
            {
                effectiveVariant = AppThemeVariant.Light;
            }
            else
            {
                effectiveVariant = AppThemeVariant.Dark;
            }
        }

        // 3. Get Palette with effective variant
        var palette = ThemePalettes.GetPalette(CurrentThemeStyle, effectiveVariant);

        // 4. Apply Palette
        foreach (var kvp in palette)
        {
            app.Resources[kvp.Key] = kvp.Value;
        }
        
        // 5. Apply Window Effects
        if (CurrentThemeStyle == AppThemeStyle.MicaCustom)
        {
            LiquidGlassEffect.ApplyToMainWindow();
        }
        else
        {
            LiquidGlassEffect.DisableForMainWindow();
        }
    }

    public List<AppThemeStyle> GetAvailableThemesForVariant(AppThemeVariant variant)
    {
        var all = Enum.GetValues(typeof(AppThemeStyle)).Cast<AppThemeStyle>().ToList();
        
        bool isDark = variant == AppThemeVariant.Dark;
        
        if (variant == AppThemeVariant.System)
        {
             var systemVariant = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
             isDark = systemVariant == PlatformThemeVariant.Dark;
        }

        if (!isDark) 
        {
            return all.Where(t => t != AppThemeStyle.MicaCustom).ToList();
        }
        else
        {
            return all;
        }
    }
}
