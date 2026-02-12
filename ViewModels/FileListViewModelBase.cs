using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CheckHash.Models;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace CheckHash.ViewModels;

public abstract partial class FileListViewModelBase : ObservableObject, IDisposable
{
    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);
    [ObservableProperty] private double _progressMax = 100;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _remainingTime = "";

    protected FileListViewModelBase()
    {
        Prefs.ForceCancelRequested += OnForceCancelRequested;
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                Localization = new LocalizationProxy(LocalizationService.Instance);
                OnPropertyChanged(nameof(TotalFilesText));
            }
        };
    }

    protected LocalizationService L => LocalizationService.Instance;
    protected ConfigurationService ConfigService => ConfigurationService.Instance;
    protected PreferencesService Prefs => PreferencesService.Instance;
    protected LoggerService Logger => LoggerService.Instance;

    public AvaloniaList<FileItem> Files { get; } = new();

    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());

    public virtual string TotalFilesText => string.Format(L["Lbl_TotalFiles"], Files.Count);

    protected abstract bool IsGlobalBusy { get; }

    protected bool CanModifyList => !IsGlobalBusy;

    public void Dispose()
    {
        Prefs.ForceCancelRequested -= OnForceCancelRequested;
        foreach (var file in Files) file.Cts?.Dispose();
    }

    private void OnForceCancelRequested(object? sender, EventArgs e)
    {
        Logger.Log($"Force Cancel requested by user ({GetType().Name}).", LogLevel.Warning);
        foreach (var file in Files) file.Cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual void ClearList()
    {
        foreach (var file in Files)
        {
            file.Cts?.Cancel();
            file.Cts?.Dispose();
        }

        Files.Clear();
        ProgressValue = 0;
        RemainingTime = "";
        OnPropertyChanged(nameof(TotalFilesText));
        NotifyCommands();
        Logger.Log(GetClearLogMessage());
    }

    protected virtual string GetClearLogMessage() => "Cleared file list.";

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual void RemoveFile(FileItem item)
    {
        if (item.IsProcessing) return;

        item.Cts?.Cancel();
        item.Cts?.Dispose();
        item.IsDeleted = true;

        if (Files.Contains(item))
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(TotalFilesText));
            NotifyCommands();
            Logger.Log($"{GetRemoveLogPrefix()}: {item.FileName}");
        }
    }

    protected virtual string GetRemoveLogPrefix() => "Removed file";

    // Abstract methods to hook into subclass specific logic
    protected abstract void NotifyCommands();

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual async Task AddFiles(Window window)
    {
        try
        {
            Logger.Log($"Opening file picker for {GetType().Name}...");
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = L["Dialog_SelectFiles"]
            });

            if (files.Count == 0) return;

            var paths = files.Select(x => x.Path.LocalPath);
            await AddFilesFromPaths(paths);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error adding files: {ex.Message}", LogLevel.Error);
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual async Task AddFolder(Window window)
    {
        try
        {
            Logger.Log($"Opening folder picker for {GetType().Name}...");
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = L["Dialog_SelectFolder"]
            });

            if (folders.Count == 0) return;

            var folderPath = folders[0].Path.LocalPath;
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_EmptyFolder"]);
                Logger.Log($"Selected folder is empty: {folderPath}", LogLevel.Warning);
                return;
            }

            await AddFilesFromPaths(files);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error adding folder: {ex.Message}", LogLevel.Error);
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], string.Format(L["Msg_OpenFolderError"], ex.Message));
        }
    }

    public abstract Task AddFilesFromPaths(IEnumerable<string> filePaths);

    protected abstract IEnumerable<FileItem> GetFailedItems();
    protected virtual string GetClearFailedLogMessage(int count) => $"Cleared {count} failed/cancelled items.";

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual async Task ClearFailedAsync()
    {
        var failedItems = GetFailedItems().ToList();
        if (failedItems.Count == 0) return;

        foreach (var item in failedItems)
        {
            item.Cts?.Cancel();
            item.Cts?.Dispose();
            item.IsDeleted = true;
            Files.Remove(item);
        }

        OnPropertyChanged(nameof(TotalFilesText));
        NotifyCommands();
        Logger.Log(GetClearFailedLogMessage(failedItems.Count));

        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
            string.Format(L["Msg_ClearedFailed"], failedItems.Count));
    }

    protected async Task RunOnUIAsync(Func<Task> action)
    {
        if (Application.Current == null)
        {
            await action();
            return;
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await action();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(action);
            }
        }
        catch
        {
            await action();
        }
    }
}
