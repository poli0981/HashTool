using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success
}

public partial class LoggerService : ObservableObject
{
    private readonly string _debugLogDir;
    private readonly string _errorLogDir;

    private readonly string _logBaseDir;
    private readonly Channel<LogWriteRequest> _logChannel;
    private readonly Channel<string> _uiLogChannel = Channel.CreateUnbounded<string>();

    // Settings
    [ObservableProperty] private bool _isRecording = true;
    [ObservableProperty] private bool _isSavingDebugLog;

    private record struct LogWriteRequest(string Directory, string Filename, string Content);

    public LoggerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logBaseDir = Path.Combine(appData, "HashTool", "log");
        _errorLogDir = Path.Combine(_logBaseDir, "errors");
        _debugLogDir = Path.Combine(_logBaseDir, "devdebug");

        EnsureDirectories();

        _logChannel = Channel.CreateUnbounded<LogWriteRequest>();
        _ = ProcessLogQueueAsync();
        _ = ProcessUiLogQueueAsync();
    }

    public static LoggerService Instance { get; } = new();

    // UI Data
    public AvaloniaList<string> Logs { get; } = new();

    private void EnsureDirectories()
    {
        if (!Directory.Exists(_errorLogDir)) Directory.CreateDirectory(_errorLogDir);
        if (!Directory.Exists(_debugLogDir)) Directory.CreateDirectory(_debugLogDir);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        message = SanitizeMessage(message);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] [{level.ToString().ToUpper()}] {message}";

        if (IsRecording)
            _uiLogChannel.Writer.TryWrite(logEntry);

        if (level == LogLevel.Error)
            WriteToFile(_errorLogDir, "error_log.txt", logEntry);
        else if (IsSavingDebugLog)
            WriteToFile(_debugLogDir, $"debug_log_{DateTime.Now:yyyyMMdd}.txt", logEntry);
    }

    public void ClearLogs()
    {
        Dispatcher.UIThread.Post(() => Logs.Clear());
    }

    private string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tempPath = Path.GetTempPath();

        var replacements = new List<(string Path, string Placeholder)>();

        if (!string.IsNullOrEmpty(userProfile) && userProfile != Path.DirectorySeparatorChar.ToString())
        {
            replacements.Add((userProfile, "[USER_PROFILE]"));
        }

        if (!string.IsNullOrEmpty(tempPath) && tempPath != Path.DirectorySeparatorChar.ToString())
        {
            var trimmedTemp = tempPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(trimmedTemp))
            {
                replacements.Add((trimmedTemp, "[TEMP]"));
            }
        }

        // Sort by length descending to replace most specific paths first
        foreach (var (path, placeholder) in replacements.OrderByDescending(x => x.Path.Length))
        {
            message = message.Replace(path, placeholder, StringComparison.OrdinalIgnoreCase);
        }

        return message;
    }

    private void WriteToFile(string dir, string filename, string content)
    {
        // Offload to background channel
        _logChannel.Writer.TryWrite(new LogWriteRequest(dir, filename, content));
    }

    private async Task ProcessUiLogQueueAsync()
    {
        while (await _uiLogChannel.Reader.WaitToReadAsync())
        {
            var batch = new List<string>();
            while (_uiLogChannel.Reader.TryRead(out var msg))
            {
                batch.Add(msg);
                if (batch.Count >= 200) break;
            }

            if (batch.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Logs.AddRange(batch);
                    if (Logs.Count > 1000)
                    {
                        Logs.RemoveRange(0, Logs.Count - 1000);
                    }
                }, DispatcherPriority.Background);

                await Task.Delay(100); // Throttle updates
            }
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        while (await _logChannel.Reader.WaitToReadAsync())
        {
            var batch = new List<LogWriteRequest>();
            while (_logChannel.Reader.TryRead(out var msg))
            {
                batch.Add(msg);
                // Limit batch size to avoid holding too much memory or delaying writes too long
                if (batch.Count >= 1000) break;
            }

            if (batch.Count == 0) continue;

            // Group by file path to minimize file open/close operations
            var fileGroups = batch.GroupBy(x => Path.Combine(x.Directory, x.Filename));

            foreach (var group in fileGroups)
            {
                try
                {
                    var sb = new StringBuilder();
                    foreach (var item in group)
                    {
                        sb.AppendLine(item.Content);
                    }

                    // Use AppendAllTextAsync which opens and closes the file.
                    // Since we batched messages, this is much more efficient than one open/close per message.
                    await File.AppendAllTextAsync(group.Key, sb.ToString());
                }
                catch
                {
                    // Ignore file write errors
                }
            }
        }
    }
}
