using System.Linq;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Infrastructure.Logging;

/// <summary>Forwards Avalonia's own warnings (binding errors, layout problems) to the app log.</summary>
public sealed class AvaloniaLogForwarder(ILogger logger) : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Write(level, area, source, messageTemplate);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Write(level, area, source, propertyValues.Length == 0
            ? messageTemplate
            : $"{messageTemplate} [{string.Join(", ", propertyValues.Select(v => v?.ToString()))}]");

    private void Write(LogEventLevel level, string area, object? source, string message)
    {
        var mapped = level switch
        {
            LogEventLevel.Fatal => LogLevel.Critical,
            LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Warning
        };
        logger.Log(mapped, "Avalonia {Area} ({Source}): {Message}", area, source?.GetType().Name, message);
    }
}
