using System;
using System.IO;
using System.Runtime.CompilerServices;
using HashTool.App.Infrastructure;

namespace HashTool.App.Tests;

/// <summary>
/// Runs before the first test touches <see cref="AppPaths"/>, which resolves its folder only once: settings, logs and crash
/// reports of every test go to a temporary folder, never the user's own.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Directory.CreateTempSubdirectory("hashtool-app-tests-").FullName);
}
