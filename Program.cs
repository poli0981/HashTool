using Avalonia;
using System;
using Velopack;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Linq;
using CheckHash.Services;
using CheckHash.Models;

namespace CheckHash;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Hook Velopack vào khởi động
        // Dòng này giúp Velopack xử lý các sự kiện install/update/uninstall
        VelopackApp.Build().Run();

        try
        {
            var config = ConfigurationService.Instance.Load();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
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
        }
        catch (Exception)
        {
            // Ignore startup errors to ensure app still tries to launch
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }
        
    private static bool IsRunAsAdmin()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                 return false;
            }

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
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
