using System;
using System.Linq;

namespace HashTool.App.Views.Controls;

/// <summary>
/// Keyboard shortcuts as tooltips show them and screen readers announce them (<c>AutomationProperties.AcceleratorKey</c>),
/// in the notation of the platform: "Ctrl+Shift+O" on Windows and Linux, "⇧⌘O" on macOS, where Command takes the place
/// of Ctrl (see the key bindings of MainWindow).
/// </summary>
public static class Shortcuts
{
    public static string AddFiles { get; } = Format("Ctrl+O");

    public static string AddFolder { get; } = Format("Ctrl+Shift+O");

    public static string Search { get; } = Format("Ctrl+F");

    public static string RunAll { get; } = Format("Ctrl+Enter");

    public static string Cancel { get; } = Format("Esc");

    internal static string Format(string gesture, bool macOS)
    {
        if (!macOS) return gesture;
        var parts = gesture.Split('+');
        var key = parts[^1] switch
        {
            "Enter" => "↩",
            var other => other
        };
        // macOS order: Control, Option, Shift, Command.
        return (parts.Contains("Alt") ? "⌥" : "") + (parts.Contains("Shift") ? "⇧" : "") + (parts.Contains("Ctrl") ? "⌘" : "") + key;
    }

    private static string Format(string gesture) => Format(gesture, OperatingSystem.IsMacOS());
}
