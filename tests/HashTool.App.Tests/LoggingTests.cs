using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>Captures what is logged through it.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

public sealed class FileLoggerTests
{
    private static string Today => DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    private static string ReadAll(string directory) =>
        string.Join("\n", Directory.GetFiles(directory, "*.log").Order(StringComparer.Ordinal).Select(f =>
        {
            using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));

    [Fact]
    public void WritesEveryLevelWithItsTag()
    {
        using var temp = new TempDirectory();
        var provider = new FileLoggerProvider(temp.Path, () => LogLevel.Trace, PathSanitizer.Shared);
        var logger = provider.CreateLogger("HashTool.App.Sample");

        logger.LogTrace("t");
        logger.LogDebug("d");
        logger.LogInformation("i");
        logger.LogWarning("w");
        logger.LogError(new InvalidOperationException("boom"), "e");
        logger.LogCritical("c");
        logger.Log((LogLevel)42, "odd");
        logger.Log(LogLevel.None, "never");
        Assert.Null(logger.BeginScope("scope"));
        provider.Flush();
        provider.Dispose();
        provider.Dispose();
        logger.LogError("after dispose");

        var text = ReadAll(temp.Path);
        foreach (var tag in new[] { "[TRC] Sample: t", "[DBG] Sample: d", "[INF] Sample: i", "[WRN] Sample: w", "[ERR] Sample: e", "[CRT] Sample: c", "[???] Sample: odd" })
            Assert.Contains(tag, text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", text, StringComparison.Ordinal);
        Assert.DoesNotContain("never", text, StringComparison.Ordinal);
        Assert.DoesNotContain("after dispose", text, StringComparison.Ordinal);
        Assert.Equal(temp.Path, provider.Directory);
    }

    [Fact]
    public void BelowTheMinimumLevel_NothingIsWritten()
    {
        using var temp = new TempDirectory();
        using var provider = new FileLoggerProvider(temp.Path, () => LogLevel.Warning, PathSanitizer.Shared);
        var logger = provider.CreateLogger("Quiet");

        logger.LogInformation("hidden");
        logger.LogWarning("shown");
        provider.Flush();

        Assert.False(logger.IsEnabled(LogLevel.Information));
        var text = ReadAll(temp.Path);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.Contains("[WRN] Quiet: shown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullFile_RollsOverToTheNextNumber()
    {
        using var temp = new TempDirectory();
        foreach (var name in new[] { $"hashtool-{Today}.log", $"hashtool-{Today}.1.log" })
        {
            using var full = File.Create(Path.Combine(temp.Path, name));
            full.SetLength(10L * 1024 * 1024);
        }

        using (var provider = new FileLoggerProvider(temp.Path, () => LogLevel.Information, PathSanitizer.Shared))
            provider.CreateLogger("Roll").LogWarning("next file");

        Assert.Contains("next file", File.ReadAllText(Path.Combine(temp.Path, $"hashtool-{Today}.2.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void FilesOlderThanTwoWeeks_AreDeleted()
    {
        using var temp = new TempDirectory();
        var oldLog = temp.File("hashtool-20000101.log", "old");
        var oldCrash = temp.File("crash-20000101-000000.txt", "old");
        var recent = temp.File("hashtool-recent.log", "recent");
        File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(oldCrash, DateTime.UtcNow.AddDays(-30));

        new FileLoggerProvider(temp.Path, () => LogLevel.Information, PathSanitizer.Shared).Dispose();

        Assert.False(File.Exists(oldLog));
        Assert.False(File.Exists(oldCrash));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void AnUnwritableFolder_NeverThrows()
    {
        using var temp = new TempDirectory();
        var blocker = temp.File("not-a-folder", "x");
        using var provider = new FileLoggerProvider(Path.Combine(blocker, "log"), () => LogLevel.Information, PathSanitizer.Shared);

        provider.CreateLogger("X").LogError("lost");
        provider.Flush();

        Assert.False(Directory.Exists(Path.Combine(blocker, "log")));
    }

    [Theory]
    [InlineData("HashTool.App.Services.UpdateService", "UpdateService")]
    [InlineData("Plain", "Plain")]
    [InlineData("Trailing.", "Trailing.")]
    public void Categories_AreShortenedToTheTypeName(string category, string expected)
    {
        Assert.Equal(expected, FileLoggerProvider.ShortCategory(category));
    }
}

public sealed class LogSinkTests
{
    [Fact]
    public void KeepsTheLatestEntries_AndReportsThem()
    {
        using var sink = new LogSink(PathSanitizer.Shared);
        var logger = sink.CreateLogger("HashTool.Test");
        var added = 0;
        sink.EntryAdded += (_, _) => added++;

        logger.LogTrace("too low");
        for (var i = 0; i <= LogSink.Capacity; i++) logger.LogInformation("entry {Number}", i);
        logger.LogError(new IOException("disk"), "failed");

        var entries = sink.Snapshot();
        Assert.Equal(LogSink.Capacity, entries.Count);
        Assert.Equal("entry 2", entries[0].Message);
        Assert.Equal("failed — IOException: disk", entries[^1].Message);
        Assert.Equal(LogSink.Capacity + 2, added);
        Assert.Null(logger.BeginScope("scope"));

        sink.Clear();
        Assert.Empty(sink.Snapshot());
    }

    [Fact]
    public void Paused_RecordsNothing()
    {
        using var sink = new LogSink(PathSanitizer.Shared) { IsRecording = false };
        var logger = sink.CreateLogger("Paused");

        logger.LogWarning("ignored");

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.Empty(sink.Snapshot());
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRACE")]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "FATAL")]
    [InlineData(LogLevel.None, "")]
    public void LevelTags(LogLevel level, string expected)
    {
        Assert.Equal(expected, new LogEntry(DateTimeOffset.UnixEpoch, level, "C", "m").LevelTag);
    }
}

public sealed class AvaloniaLogForwarderTests
{
    [Fact]
    public void ForwardsWarningsAndErrorsToTheAppLog()
    {
        var logger = new RecordingLogger();
        var forwarder = new AvaloniaLogForwarder(logger);

        Assert.True(forwarder.IsEnabled(LogEventLevel.Warning, "Binding"));
        Assert.False(forwarder.IsEnabled(LogEventLevel.Information, "Binding"));

        forwarder.Log(LogEventLevel.Warning, "Binding", this, "Path not found");
        forwarder.Log(LogEventLevel.Error, "Layout", null, "Measure failed", 1, null);
        forwarder.Log(LogEventLevel.Fatal, "Render", null, "Lost device", []);

        Assert.Equal([LogLevel.Warning, LogLevel.Error, LogLevel.Critical], logger.Entries.Select(e => e.Level));
        Assert.Contains("Avalonia Binding (AvaloniaLogForwarderTests): Path not found", logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("Measure failed [1, ]", logger.Entries[1].Message, StringComparison.Ordinal);
        Assert.EndsWith("Lost device", logger.Entries[2].Message, StringComparison.Ordinal);
    }
}

public sealed class CrashReporterTests
{
    [AvaloniaFact]
    public void Report_WritesACrashFileIntoTheLogFolder()
    {
        var path = CrashReporter.Report(new InvalidOperationException("crash test"), isTerminating: false);

        Assert.NotNull(path);
        Assert.StartsWith(Path.GetFullPath(AppPaths.LogDirectory), path, StringComparison.Ordinal);
        var text = File.ReadAllText(path);
        Assert.Contains($"Hash Tool {CrashReporter.Version}", text, StringComparison.Ordinal);
        Assert.Contains("crash test", text, StringComparison.Ordinal);
        Assert.Contains("(no exception object)", File.ReadAllText(CrashReporter.Report(null, isTerminating: false)!), StringComparison.Ordinal);
    }

    [Fact]
    public void TestsNeverUseTheRealDataFolder()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(AppPaths.DataDirectory), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task UnobservedTaskExceptions_AreLoggedNotFatal()
    {
        var sink = AppHost.Get<LogSink>();
        var before = sink.Snapshot().Count(e => e.Message.Contains("Unobserved task exception", StringComparison.Ordinal));

        Abandon();
        for (var i = 0; i < 20 && sink.Snapshot().Count(e => e.Message.Contains("Unobserved task exception", StringComparison.Ordinal)) == before; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }

        Assert.True(sink.Snapshot().Count(e => e.Message.Contains("Unobserved task exception", StringComparison.Ordinal)) > before);

        static void Abandon() => _ = Task.Run(() => throw new InvalidOperationException("nobody waits for me"));
    }

    [AvaloniaFact]
    public void UnhandledUiExceptions_AreLoggedAndShown()
    {
        var sink = AppHost.Get<LogSink>();

        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("ui failure"));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(sink.Snapshot(), e => e.Message.Contains("Unhandled UI exception", StringComparison.Ordinal) && e.Message.Contains("ui failure", StringComparison.Ordinal));
    }
}
