using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;

namespace CheckHash.Services.ThemeEffects;

public static class WindowEffect
{
    public static void ApplyToMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            if (desktop.MainWindow is Window window)
                Apply(window);
    }

    public static void DisableForMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            if (desktop.MainWindow is Window window)
                Disable(window);
    }

    public static void Apply(Window window)
    {
        if (window == null) return;

        window.Background = Brushes.Transparent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ApplyMacOS(window);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ApplyWindows(window);
        else
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
    }

    public static void Disable(Window window)
    {
        if (window == null) return;

        window.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
    }
    private static void ApplyMacOS(Window window)
    {
        window.TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur
        };

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
    }

    private static void ApplyWindows(Window window)
    {
        window.TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur
        };

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
    }
}