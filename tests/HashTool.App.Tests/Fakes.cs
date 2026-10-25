using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core.Configuration;
using HashTool.Core.Hashing;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Tests;

/// <summary>Records dialogs instead of showing them. Confirmations get <see cref="Answer"/> (no by default).</summary>
internal sealed class RecordingDialogs : IDialogService
{
    public List<(string Message, DialogIcon Icon)> Shown { get; } = [];

    public List<string> Confirmations { get; } = [];

    public List<string?> ErrorDetails { get; } = [];

    public Func<string, bool> Answer { get; init; } = _ => false;

    public Task ShowAsync(string title, string message, DialogIcon icon = DialogIcon.Information)
    {
        Shown.Add((message, icon));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, DialogIcon icon = DialogIcon.Question, string? yesText = null,
        string? noText = null)
    {
        Confirmations.Add(message);
        // Never accept the restart as administrator: it would start the test host again, elevated.
        return Task.FromResult(title != AppHost.Get<LocalizationService>()["Btn_RestartAdmin"] && Answer(message));
    }

    public Task ShowErrorAsync(string title, string message, string? details = null)
    {
        Shown.Add((message, DialogIcon.Error));
        ErrorDetails.Add(details);
        return Task.CompletedTask;
    }
}

/// <summary>File pickers that return what the test prepared; the save dialog is cancelled unless a path is set.</summary>
internal sealed class ScriptedStorage : IStorageService
{
    public IReadOnlyList<string> Files { get; set; } = [];

    public IReadOnlyList<string> Folders { get; set; } = [];

    public string? SavePath { get; set; }

    public FileTypeChoice? SaveType { get; set; }

    /// <summary>Picks the file type in the save dialog from the offered ones (instead of <see cref="SaveType"/>).</summary>
    public Func<IReadOnlyList<FileTypeChoice>, FileTypeChoice?>? PickType { get; set; }

    public string? SuggestedName { get; private set; }

    public IReadOnlyList<FileTypeChoice> OfferedTypes { get; private set; } = [];

    public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, IReadOnlyList<FileTypeChoice>? types = null) =>
        Task.FromResult(Files);

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title) => Task.FromResult(Folders);

    public Task<(string Path, FileTypeChoice? Type)?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<FileTypeChoice> types)
    {
        SuggestedName = suggestedName;
        OfferedTypes = types;
        var type = PickType is null ? SaveType : PickType(types);
        return Task.FromResult<(string Path, FileTypeChoice? Type)?>(SavePath is null ? null : (SavePath, type));
    }
}

internal sealed class RecordingClipboard : IClipboardService
{
    public List<string> Texts { get; } = [];

    public Task SetTextAsync(string text)
    {
        Texts.Add(text);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingLauncher : ILauncherService
{
    public List<string> Urls { get; } = [];

    public List<string> Folders { get; } = [];

    public Task OpenUrlAsync(string url)
    {
        Urls.Add(url);
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string path)
    {
        Folders.Add(path);
        return Task.CompletedTask;
    }
}

/// <summary>Services for a fresh page view model; settings are the shared ones unless a test brings its own.</summary>
internal static class TestServices
{
    public static ILoggerFactory Loggers => AppHost.Get<ILoggerFactory>();

    public static LocalizationService Localization => AppHost.Get<LocalizationService>();

    /// <summary>Settings of their own in <paramref name="directory"/>, for tests that change limits or preferences.</summary>
    public static SettingsService Settings(string directory) =>
        new(new SettingsStore(directory, Loggers.CreateLogger<SettingsStore>()), Loggers.CreateLogger<SettingsService>());

    public static FileListServices Create(RecordingDialogs? dialogs = null, ScriptedStorage? storage = null, RecordingClipboard? clipboard = null,
        SettingsService? settings = null) =>
        new(Localization, settings ?? AppHost.Get<SettingsService>(), dialogs ?? new RecordingDialogs(), storage ?? new ScriptedStorage(),
            clipboard ?? new RecordingClipboard(), new HashEngine(), Loggers);
}

internal static class Drops
{
    /// <summary>The data a file manager drop carries for these files.</summary>
    public static async Task<DataTransfer> FilesAsync(TopLevel topLevel, params string[] paths)
    {
        var data = new DataTransfer();
        foreach (var path in paths)
        {
            var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.GetFullPath(path)))
                       ?? throw new FileNotFoundException("Not found.", path);
            var item = new DataTransferItem();
            item.SetFile(file);
            data.Add(item);
        }

        return data;
    }
}

internal static class Wait
{
    /// <summary>Runs the UI loop until <paramref name="condition"/> holds; fails instead of hanging the test run.</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The expected state was not reached in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }
}
