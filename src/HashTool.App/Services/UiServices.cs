using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using HashTool.App.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Services;

public enum DialogIcon
{
    None,
    Information,
    Warning,
    Error,
    Success,
    Question
}

/// <summary>Message boxes. Implemented with <see cref="DialogWindow"/>; tests substitute a fake.</summary>
public interface IDialogService
{
    Task ShowAsync(string title, string message, DialogIcon icon = DialogIcon.Information);

    Task<bool> ConfirmAsync(string title, string message, DialogIcon icon = DialogIcon.Question, string? yesText = null,
        string? noText = null);

    /// <summary>Shows an error with a button that copies the technical details.</summary>
    Task ShowErrorAsync(string title, string message, string? details = null);
}

/// <summary>A file type offered in a save dialog.</summary>
public sealed record FileTypeChoice(string Name, string Pattern, object? Tag = null);

/// <summary>Native file and folder pickers.</summary>
public interface IStorageService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, IReadOnlyList<FileTypeChoice>? types = null);

    Task<IReadOnlyList<string>> PickFoldersAsync(string title);

    /// <summary>Returns the chosen path and the file type picked in the dialog (if the platform reports it).</summary>
    Task<(string Path, FileTypeChoice? Type)?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<FileTypeChoice> types);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>Opens web links and folders with the system's default handler.</summary>
public interface ILauncherService
{
    Task OpenUrlAsync(string url);

    Task OpenFolderAsync(string path);
}

/// <summary>Gives services access to the main window once it exists.</summary>
public sealed class MainWindowAccessor
{
    public Window? Window =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;

    public TopLevel? TopLevel => Window;
}

public sealed class DialogService(MainWindowAccessor main, LocalizationService localization) : IDialogService
{
    public Task ShowAsync(string title, string message, DialogIcon icon = DialogIcon.Information) =>
        DialogWindow.ShowAsync(main.Window, localization, title, message, icon);

    public Task<bool> ConfirmAsync(string title, string message, DialogIcon icon = DialogIcon.Question, string? yesText = null,
        string? noText = null) =>
        DialogWindow.ConfirmAsync(main.Window, localization, title, message, icon, yesText ?? localization["Btn_Yes"],
            noText ?? localization["Btn_No"]);

    public Task ShowErrorAsync(string title, string message, string? details = null) =>
        DialogWindow.ShowAsync(main.Window, localization, title, message, DialogIcon.Error, details);
}

public sealed class StorageService(MainWindowAccessor main) : IStorageService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, IReadOnlyList<FileTypeChoice>? types = null)
    {
        if (main.TopLevel?.StorageProvider is not { } storage) return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = types?.Select(ToFilePickerType).ToList()
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync(string title)
    {
        if (main.TopLevel?.StorageProvider is not { } storage) return [];
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = true });
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<(string Path, FileTypeChoice? Type)?> SaveFileAsync(string title, string suggestedName, IReadOnlyList<FileTypeChoice> types)
    {
        if (main.TopLevel?.StorageProvider is not { } storage) return null;
        var pickerTypes = types.Select(ToFilePickerType).ToList();
        var result = await storage.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
            FileTypeChoices = pickerTypes,
            ShowOverwritePrompt = true
        });
        if (result.File?.TryGetLocalPath() is not { } path) return null;

        var index = result.SelectedFileType is { } selected ? pickerTypes.FindIndex(t => t.Name == selected.Name) : -1;
        var type = index >= 0 ? types[index] : types.FirstOrDefault(t => path.EndsWith(t.Pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase));
        return (path, type);
    }

    private static FilePickerFileType ToFilePickerType(FileTypeChoice choice) => new(choice.Name) { Patterns = [choice.Pattern] };
}

public sealed class ClipboardService(MainWindowAccessor main) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (main.TopLevel?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}

public sealed class LauncherService(MainWindowAccessor main, ILogger<LauncherService> logger) : ILauncherService
{
    public async Task OpenUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            logger.LogWarning("Refused to open {Url}", url);
            return;
        }

        if (main.TopLevel?.Launcher is { } launcher && await launcher.LaunchUriAsync(uri)) return;
        logger.LogWarning("Could not open {Url}", url);
    }

    public async Task OpenFolderAsync(string path)
    {
        // Only folders inside the app's data directory are opened (logs, settings).
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(AppPaths.DataDirectory);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
        {
            logger.LogWarning("Refused to open folder {Path}", path);
            return;
        }

        if (main.TopLevel?.Launcher is { } launcher && await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(full))) return;
        logger.LogWarning("Could not open folder {Path}", path);
    }
}
