using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using HashTool.App.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Infrastructure;

/// <summary>Writes a crash report synchronously before the process dies, then flushes the regular log.</summary>
public static class CrashReporter
{
    private static FileLoggerProvider? _fileLogger;
    private static ILogger? _logger;

    public static void Install(FileLoggerProvider fileLogger, ILogger logger)
    {
        _fileLogger = fileLogger;
        _logger = logger;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static string? Report(Exception? exception, bool isTerminating = true)
    {
        try
        {
            _logger?.LogCritical(exception, "Unhandled exception (terminating: {Terminating})", isTerminating);
            _fileLogger?.Flush();

            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory,
                $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
            var text = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"Hash Tool {Version}")
                .AppendLine(CultureInfo.InvariantCulture, $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}")
                .AppendLine(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
                .AppendLine()
                .AppendLine(PathSanitizer.Shared.Sanitize(exception?.ToString() ?? "(no exception object)"))
                .ToString();
            File.WriteAllText(path, text);
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static string Version =>
        typeof(CrashReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CrashReporter).Assembly.GetName().Version?.ToString() ?? "unknown";
}
