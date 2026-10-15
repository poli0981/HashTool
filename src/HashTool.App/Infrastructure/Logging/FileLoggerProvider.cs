using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Infrastructure.Logging;

/// <summary>
/// Writes <c>hashtool-yyyyMMdd.log</c> files (invariant culture, ISO-8601 timestamps), keeps 14 days and at most 10 MB per
/// file. Warnings and errors are flushed immediately so they survive a crash.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileSize = 10L * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly string _directory;
    private readonly Func<LogLevel> _minimumLevel;
    private readonly PathSanitizer _sanitizer;
    private readonly Lock _gate = new();
    private readonly Timer _flushTimer;
    private StreamWriter? _writer;
    private string? _currentPath;
    private bool _disposed;

    public FileLoggerProvider(string directory, Func<LogLevel> minimumLevel, PathSanitizer sanitizer)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _sanitizer = sanitizer;
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        DeleteOldFiles();
    }

    public string Directory => _directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortCategory(categoryName));

    public void Flush()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Flush();
            }
            catch (IOException)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel();

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var builder = new StringBuilder(128)
            .Append(now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelTag(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(_sanitizer.Sanitize(message));
        if (exception is not null) builder.AppendLine().Append(_sanitizer.Sanitize(exception.ToString()));

        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var writer = EnsureWriter(now);
                writer.WriteLine(builder.ToString());
                if (level >= LogLevel.Warning) writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private StreamWriter EnsureWriter(DateTimeOffset now)
    {
        var path = Path.Combine(_directory, $"hashtool-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        if (_writer is not null && path == _currentPath && _writer.BaseStream.Length < MaxFileSize) return _writer;

        _writer?.Dispose();
        System.IO.Directory.CreateDirectory(_directory);
        if (File.Exists(path) && new FileInfo(path).Length >= MaxFileSize)
        {
            for (var i = 1; ; i++)
            {
                var rolled = Path.ChangeExtension(path, $".{i}.log");
                if (!File.Exists(rolled) || new FileInfo(rolled).Length < MaxFileSize)
                {
                    path = rolled;
                    break;
                }
            }
        }

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
        _currentPath = path;
        return _writer;
    }

    private void DeleteOldFiles()
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory)) return;
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.log")
                         .Concat(System.IO.Directory.EnumerateFiles(_directory, "crash-*.txt")))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    internal static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 && dot < category.Length - 1 ? category[(dot + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
