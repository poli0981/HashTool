using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using HashTool.App.Infrastructure;
using HashTool.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace HashTool.App.Tests;

/// <summary>Runs the real <see cref="App"/> on Avalonia's headless platform, with settings and logs in a temporary folder.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Directory.CreateTempSubdirectory("hashtool-app-tests-").FullName);
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
    }
}
