using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace HashTool.App.Services.Theme;

/// <summary>Mica/acrylic window backgrounds for the Fluent and Glassmorphism styles.</summary>
public static class WindowEffect
{
    public static void ApplyToMainWindow()
    {
        if (MainWindow() is { } window) Apply(window);
    }

    public static void DisableForMainWindow()
    {
        if (MainWindow() is { } window) Disable(window);
    }

    public static void Apply(Window window)
    {
        if (OperatingSystem.IsWindows())
            window.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
        else if (OperatingSystem.IsMacOS())
            window.TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
        else
        {
            Disable(window);
            return;
        }

        window.Background = Brushes.Transparent;
    }

    public static void Disable(Window window)
    {
        window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        window.Bind(TemplatedControl.BackgroundProperty, window.GetResourceObservable("AppBackgroundBrush"));
    }

    private static Window? MainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;
}
