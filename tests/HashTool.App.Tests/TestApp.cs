using Avalonia;
using Avalonia.Headless;
using HashTool.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

// One application for the whole run, like the app itself. With a new one per test, the asynchronous part of the app's
// startup (font loading, then applying the settings) of one test lands on the dispatcher of a later one.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace HashTool.App.Tests;

/// <summary>Runs the real <see cref="App"/> on Avalonia's headless platform (data folder: see <see cref="TestEnvironment"/>).</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
}
