using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.Services;
using Microsoft.Extensions.Logging;

namespace HashTool.App.ViewModels;

/// <summary>Live log viewer shown in Developer Mode.</summary>
public sealed partial class DeveloperViewModel : ObservableObject
{
    private readonly LogSink _sink;
    private readonly IClipboardService _clipboard;
    private readonly IStorageService _storage;
    private readonly ILauncherService _launcher;
    private readonly ConcurrentQueue<LogEntry> _incoming = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private LocalizationProxy _localization;
    [ObservableProperty] private LogLevel _minimumLevel = LogLevel.Debug;
    [ObservableProperty] private bool _autoScroll = true;

    public DeveloperViewModel(LogSink sink, LocalizationService localization, IClipboardService clipboard, IStorageService storage,
        ILauncherService launcher)
    {
        _sink = sink;
        _clipboard = clipboard;
        _storage = storage;
        _launcher = launcher;
        _localization = new LocalizationProxy(localization);
        localization.LanguageChanged += (_, _) => Localization = new LocalizationProxy(localization);

        Entries.AddRange(sink.Snapshot());
        sink.EntryAdded += (_, entry) => _incoming.Enqueue(entry);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Flush());
        _timer.Start();
    }

    /// <summary>Entries at or above <see cref="MinimumLevel"/>, newest last.</summary>
    public AvaloniaList<LogEntry> Entries { get; } = [];

    public IReadOnlyList<LogLevel> Levels { get; } = [LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error];

    public bool IsRecording
    {
        get => _sink.IsRecording;
        set
        {
            _sink.IsRecording = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Raised after new entries were added, so the view can scroll to the end.</summary>
    public event EventHandler? EntriesAppended;

    partial void OnMinimumLevelChanged(LogLevel value)
    {
        Entries.Clear();
        Entries.AddRange(_sink.Snapshot().Where(e => e.Level >= value));
    }

    private void Flush()
    {
        var added = new List<LogEntry>();
        while (_incoming.TryDequeue(out var entry))
            if (entry.Level >= MinimumLevel) added.Add(entry);
        if (added.Count == 0) return;

        Entries.AddRange(added);
        var overflow = Entries.Count - LogSink.Capacity;
        if (overflow > 0) Entries.RemoveRange(0, overflow);
        if (AutoScroll) EntriesAppended?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Clear()
    {
        _sink.Clear();
        Entries.Clear();
    }

    [RelayCommand]
    private Task CopyAsync() => _clipboard.SetTextAsync(string.Join(Environment.NewLine, Entries.Select(e => e.ToString())));

    [RelayCommand]
    private async Task ExportAsync()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = await _storage.SaveFileAsync(Localization["Dev_Export"], $"hashtool-log-{stamp}.txt",
            [new FileTypeChoice("Text", "*.txt")]);
        if (target is { } t)
            await File.WriteAllTextAsync(t.Path, string.Join(Environment.NewLine, Entries.Select(e => e.ToString())), new UTF8Encoding(false));
    }

    [RelayCommand]
    private Task OpenLogFolderAsync()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        return _launcher.OpenFolderAsync(AppPaths.LogDirectory);
    }
}
