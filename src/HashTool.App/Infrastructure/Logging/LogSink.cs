using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Infrastructure.Logging;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message)
{
    public string LevelTag => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "FATAL",
        _ => ""
    };

    public override string ToString() =>
        $"{Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{LevelTag}] {Category}: {Message}";
}

/// <summary>Keeps the most recent log entries in memory for the Developer console.</summary>
public sealed class LogSink : ILoggerProvider
{
    public const int Capacity = 2000;

    private readonly Queue<LogEntry> _entries = new(Capacity);
    private readonly PathSanitizer _sanitizer;
    private readonly object _gate = new();

    public LogSink(PathSanitizer sanitizer) => _sanitizer = sanitizer;

    /// <summary>Raised on the logging thread for every entry.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    /// <summary>When false, entries are not recorded (the console's pause button).</summary>
    public bool IsRecording { get; set; } = true;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, FileLoggerProvider.ShortCategory(categoryName));

    public void Dispose()
    {
    }

    private void Add(LogLevel level, string category, string message, Exception? exception)
    {
        if (!IsRecording) return;
        if (exception is not null) message = $"{message} — {exception.GetType().Name}: {exception.Message}";
        var entry = new LogEntry(DateTimeOffset.Now, level, category, _sanitizer.Sanitize(message));
        lock (_gate)
        {
            if (_entries.Count == Capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    private sealed class SinkLogger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug && sink.IsRecording;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) sink.Add(logLevel, category, formatter(state, exception), exception);
        }
    }
}
