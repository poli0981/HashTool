using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CheckHash.Models;
using CheckHash.Services.ThemeEffects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class ThemeService : ObservableObject
{
    [ObservableProperty] private AppThemeStyle _currentThemeStyle = AppThemeStyle.Fluent;
    [ObservableProperty] private AppThemeVariant _currentThemeVariant = AppThemeVariant.System;
    [ObservableProperty] private bool _isThemeLocked;

    public ThemeService()
    {
        if (Application.Current?.PlatformSettings != null)
            Application.Current.PlatformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
    }

    public static ThemeService Instance { get; } = new();

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (CurrentThemeVariant == AppThemeVariant.System) Dispatcher.UIThread.Post(() => ApplyTheme());
    }

    partial void OnCurrentThemeStyleChanged(AppThemeStyle value)
    {
        ApplyTheme();
    }

    partial void OnCurrentThemeVariantChanged(AppThemeVariant value)
    {
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null) return;

        // 1. Apply Theme Variant
        var requestedVariant = CurrentThemeVariant switch
        {
            AppThemeVariant.Light => ThemeVariant.Light,
            AppThemeVariant.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (app.RequestedThemeVariant != requestedVariant) app.RequestedThemeVariant = requestedVariant;

        // 2. Determine effective variant for palette logic
        var effectiveVariant = CurrentThemeVariant;
        if (CurrentThemeVariant == AppThemeVariant.System)
        {
            var systemVariant = app.PlatformSettings?.GetColorValues().ThemeVariant;
            if (systemVariant == PlatformThemeVariant.Light)
                effectiveVariant = AppThemeVariant.Light;
            else
                effectiveVariant = AppThemeVariant.Dark;
        }

        // 3. Get Palette with effective variant
        var palette = ThemePalettes.GetPalette(CurrentThemeStyle, effectiveVariant);

        // 4. Apply Palette
        // Note: Explicitly remove before adding to ensure change notification propagates immediately,
        // specifically for switching between Light/Dark variants of the same theme (e.g. Glassmorphism).
        foreach (var kvp in palette)
        {
            if (app.Resources.ContainsKey(kvp.Key))
            {
                app.Resources.Remove(kvp.Key);
            }
            app.Resources[kvp.Key] = kvp.Value;
        }

        // 5. Apply Window Effects
        if (CurrentThemeStyle == AppThemeStyle.Fluent || CurrentThemeStyle == AppThemeStyle.Glassmorphism)
            WindowEffect.ApplyToMainWindow();
        else
            WindowEffect.DisableForMainWindow();
    }

    public List<AppThemeStyle> GetAvailableThemesForVariant(AppThemeVariant variant)
    {
        return Enum.GetValues(typeof(AppThemeStyle)).Cast<AppThemeStyle>().ToList();
    }
}