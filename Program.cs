using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Avalonia;
using CheckHash.Services;
using Velopack;

namespace CheckHash;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        try
        {
            ConfigurationService.Instance.EnsureConfigFileExists();
            var config = ConfigurationService.Instance.Load();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                if (config.IsAdminModeEnabled && !IsRunAsAdmin())
                {
                    var moduleName = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        var arguments = string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg));
                        var processInfo = new ProcessStartInfo(moduleName)
                        {
                            UseShellExecute = true,
                            Verb = "runas",
                            Arguments = arguments
                        };

                        try
                        {
                            Process.Start(processInfo);
                            return;
                        }
                        catch (Exception)
                        {
                            // User cancelled UAC or other error, continue as normal user
                        }
                    }
                }
        }
        catch (Exception ex)
        {
            // Ignore startup errors to ensure app still tries to launch, but log them
            LoggerService.Instance.Log($"Startup error: {ex.Message}", LogLevel.Error);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static bool IsRunAsAdmin()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}