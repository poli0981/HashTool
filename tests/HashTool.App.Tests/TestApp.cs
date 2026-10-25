using Avalonia;
using Avalonia.Headless;
using HashTool.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace HashTool.App.Tests;

/// <summary>Runs the real <see cref="App"/> on Avalonia's headless platform (data folder: see <see cref="TestEnvironment"/>).</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
}
