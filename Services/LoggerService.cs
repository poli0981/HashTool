using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public enum LogLevel { Info, Warning, Error, Success }

public partial class LoggerService : ObservableObject
{
    public static LoggerService Instance { get; } = new();

    private readonly string _logBaseDir;
    private readonly string _errorLogDir;
    private readonly string _debugLogDir;

    // UI Data
    public ObservableCollection<string> Logs { get; } = new();
    
    // Settings
    [ObservableProperty] private bool _isRecording = true; // Write logs to UI by default
    [ObservableProperty] private bool _isSavingDebugLog = false; // Save debug logs to files by default

    public LoggerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logBaseDir = Path.Combine(appData, "CheckHash", "log");
        _errorLogDir = Path.Combine(_logBaseDir, "errors");
        _debugLogDir = Path.Combine(_logBaseDir, "devdebug");

        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        if (!Directory.Exists(_errorLogDir)) Directory.CreateDirectory(_errorLogDir);
        if (!Directory.Exists(_debugLogDir)) Directory.CreateDirectory(_debugLogDir);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{timestamp}] [{level.ToString().ToUpper()}] {message}";

        // 1. Write in UI (Real-time)
        if (IsRecording)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Logs.Add(logEntry);
                // Limit the number of logs to prevent memory issues (keep last 1000 logs)
                if (Logs.Count > 1000) Logs.RemoveAt(0);
            });
        }

        // 2. Save to files
        if (level == LogLevel.Error)
        {
            // Error log always saved
            WriteToFile(_errorLogDir, "error_log.txt", logEntry);
        }
        else if (IsSavingDebugLog)
        {
            // Debug log saved only if enabled
            WriteToFile(_debugLogDir, $"debug_log_{DateTime.Now:yyyyMMdd}.txt", logEntry);
        }
    }

    public void ClearLogs()
    {
        Dispatcher.UIThread.Post(() => Logs.Clear());
    }

    private object _fileLock = new();
    private void WriteToFile(string dir, string filename, string content)
    {
        lock (_fileLock)
        {
            try
            {
                var path = Path.Combine(dir, filename);
                File.AppendAllText(path, content + Environment.NewLine);
            }
            catch
            {
                // Ignore file write errors
            }
        }
    }
}