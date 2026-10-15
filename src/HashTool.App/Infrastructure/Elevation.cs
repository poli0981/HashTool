using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace HashTool.App.Infrastructure;

/// <summary>Administrator rights on Windows: detection and an explicit "restart as administrator".</summary>
public static class Elevation
{
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    public static bool CanRestartElevated => OperatingSystem.IsWindows() && !IsElevated && Environment.ProcessPath is not null;

    /// <summary>
    /// Starts an elevated copy that reopens <paramref name="paths"/>. Returns false if the user declined the UAC prompt.
    /// </summary>
    public static bool RestartElevated(ActivationVerb verb, IReadOnlyCollection<string> paths)
    {
        if (!CanRestartElevated) return false;

        string? list = null;
        try
        {
            list = Path.Combine(Path.GetTempPath(), $"hashtool-{Guid.NewGuid():N}.txt");
            File.WriteAllLines(list, paths);
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                ArgumentList =
                {
                    verb == ActivationVerb.Verify ? "--verify" : "--create",
                    "--wait-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                    "--files-from", list
                }
            };
            return Process.Start(start) is not null;
        }
        catch (Win32Exception)
        {
            // The user cancelled the UAC prompt.
            if (list is not null) File.Delete(list);
            return false;
        }
    }

    /// <summary>Waits briefly for the process that launched us (restart handoff) to exit.</summary>
    public static void WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
