using System;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HashTool.Core.Configuration;

namespace HashTool.App.Services.Theme;

/// <summary>Applies the light/dark variant, the colour palette of the selected style and window effects.</summary>
public sealed partial class ThemeService : ObservableObject
{
    [ObservableProperty] private AppThemeStyle _currentThemeStyle = AppThemeStyle.Fluent;
    [ObservableProperty] private AppThemeVariant _currentThemeVariant = AppThemeVariant.System;
    [ObservableProperty] private bool _isThemeLocked;

    private IPlatformSettings? _platformSettings;

    public static AppThemeStyle[] Styles { get; } = Enum.GetValues<AppThemeStyle>();

    /// <summary>Starts following the operating system's light/dark setting. Call once the application exists.</summary>
    public void Attach(Application application)
    {
        _platformSettings = application.PlatformSettings;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += (_, _) =>
            {
                if (CurrentThemeVariant == AppThemeVariant.System) Dispatcher.UIThread.Post(Apply);
            };
        }

        Apply();
    }

    partial void OnCurrentThemeStyleChanged(AppThemeStyle value) => Apply();

    partial void OnCurrentThemeVariantChanged(AppThemeVariant value) => Apply();

    public bool IsDark
    {
        get
        {
            return CurrentThemeVariant switch
            {
                AppThemeVariant.Dark => true,
                AppThemeVariant.Light => false,
                _ => _platformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            };
        }
    }

    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = CurrentThemeVariant switch
        {
            AppThemeVariant.Light => ThemeVariant.Light,
            AppThemeVariant.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        foreach (var (key, value) in ThemePalettes.Get(CurrentThemeStyle, IsDark))
            app.Resources[key] = value;

        if (CurrentThemeStyle is AppThemeStyle.Fluent or AppThemeStyle.Glassmorphism) WindowEffect.ApplyToMainWindow();
        else WindowEffect.DisableForMainWindow();
        OnPropertyChanged(nameof(IsDark));
    }
}
