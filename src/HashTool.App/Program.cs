using System;
using Avalonia;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.Services;
using Microsoft.Extensions.Logging;
using Velopack;

namespace HashTool.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles Velopack install/update/uninstall hooks and exits for them.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows()) velopack.OnBeforeUninstallFastCallback(_ => ShellIntegration.TryUnregister());
        velopack.Run();

        var commandLine = AppCommandLine.Parse(args);
        if (commandLine.SmokeTest) return SmokeTest.Run(commandLine);
        if (commandLine.WaitPid is { } pid) Elevation.WaitForExit(pid);

        using var instance = SingleInstance.Create(Elevation.IsElevated);
        if (!instance.TryBecomePrimary() && instance.TryForward(commandLine.ToRequest(), TimeSpan.FromSeconds(3))) return 0;

        try
        {
            AppHost.Initialize();
            var activation = AppHost.Get<ActivationService>();
            activation.Enqueue(commandLine.ToRequest());
            instance.StartServer(activation.Enqueue);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex);
            throw;
        }
        finally
        {
            AppHost.Shutdown();
        }
    }

    /// <summary>Also used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .AfterSetup(_ =>
            {
                if (AppHost.IsInitialized)
                    Avalonia.Logging.Logger.Sink = new AvaloniaLogForwarder(AppHost.Get<ILoggerFactory>().CreateLogger("Avalonia"));
            });
}
