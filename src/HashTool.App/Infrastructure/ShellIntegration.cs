using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HashTool.Core.Checksums;
using Microsoft.Win32;

namespace HashTool.App.Infrastructure;

/// <summary>
/// Optional Explorer context-menu entries ("Hash with Hash Tool", "Verify with Hash Tool"), written per user under
/// <c>HKCU\Software\Classes</c> so no administrator rights are needed. On Windows 11 they appear under "Show more options".
/// </summary>
public static partial class ShellIntegration
{
    private const string CreateVerb = "HashTool.Create";
    private const string VerifyVerb = "HashTool.Verify";
    private const string OwnerValue = "HashToolOwner";

    /// <summary>
    /// Checksum file types that get "Verify with Hash Tool": every type Hash Tool reads. The entries are written again at
    /// each start (see SettingsViewModel.RefreshShellIntegration), so new types reach existing installations.
    /// </summary>
    private static IReadOnlyList<string> ChecksumExtensions => ChecksumFileNames.KnownExtensions;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Whether the entries exist and point at <paramref name="executable"/>.</summary>
    public static bool IsRegistered(string executable)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\*\shell\{CreateVerb}");
        return string.Equals(key?.GetValue(OwnerValue) as string, executable, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(string executable, string createLabel, string verifyLabel)
    {
        if (!OperatingSystem.IsWindows()) return;
        var icon = $"\"{executable}\",0";
        WriteVerb(@"Software\Classes\*\shell", CreateVerb, createLabel, icon, $"\"{executable}\" --create \"%1\"", executable, multiSelect: true);
        WriteVerb(@"Software\Classes\*\shell", VerifyVerb, verifyLabel, icon, $"\"{executable}\" --verify \"%1\"", executable, multiSelect: true);
        WriteVerb(@"Software\Classes\Directory\shell", CreateVerb, createLabel, icon, $"\"{executable}\" --create \"%1\"", executable, multiSelect: true);
        WriteVerb(@"Software\Classes\Directory\Background\shell", CreateVerb, createLabel, icon, $"\"{executable}\" --create \"%V\"", executable, multiSelect: false);
        foreach (var extension in ChecksumExtensions)
        {
            WriteVerb($@"Software\Classes\SystemFileAssociations\{extension}\shell", VerifyVerb, verifyLabel, icon,
                $"\"{executable}\" --verify \"%1\"", executable, multiSelect: true);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;
        var roots = new[] { @"Software\Classes\*\shell", @"Software\Classes\Directory\shell", @"Software\Classes\Directory\Background\shell" }
            .Concat(ChecksumExtensions.Select(e => $@"Software\Classes\SystemFileAssociations\{e}\shell"));
        foreach (var root in roots)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{CreateVerb}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{VerifyVerb}", throwOnMissingSubKey: false);
        }

        NotifyShell();
    }

    /// <summary>Used by the Velopack uninstall hook: must be fast and must never throw.</summary>
    public static void TryUnregister()
    {
        try
        {
            Unregister();
        }
        catch
        {
            // Uninstall must not fail because of the context menu.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteVerb(string root, string verb, string label, string icon, string command, string executable, bool multiSelect)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{root}\{verb}");
        key.SetValue("MUIVerb", label);
        key.SetValue("Icon", icon);
        key.SetValue(OwnerValue, executable);
        if (multiSelect) key.SetValue("MultiSelectModel", "Player");
        using var commandKey = key.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }

    private static void NotifyShell()
    {
        if (OperatingSystem.IsWindows()) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
