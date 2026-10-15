using System;
using System.IO;
using HashTool.Core.Configuration;

namespace HashTool.App.Infrastructure;

/// <summary>
/// Where the app keeps its data. <c>HASHTOOL_DATA_DIR</c> overrides the location (used by tests and smoke runs so they never
/// touch the user's real settings).
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "HASHTOOL_DATA_DIR";

    public static string DataDirectory { get; } = Resolve();

    public static string SettingsDirectory => Path.Combine(DataDirectory, "settings");

    public static string LogDirectory => Path.Combine(DataDirectory, "log");

    private static string Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return string.IsNullOrWhiteSpace(overridden) ? SettingsStore.DefaultDataDirectory : Path.GetFullPath(overridden);
    }
}
