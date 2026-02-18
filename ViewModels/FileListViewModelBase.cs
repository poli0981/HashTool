using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
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

namespace CheckHash.ViewModels;

public abstract partial class FileListViewModelBase : ObservableObject, IDisposable
{
    protected CancellationTokenSource? _batchCts;
    [ObservableProperty] private int _cancelledCount;
    [ObservableProperty] private string _detailedStatsText = "";
    [ObservableProperty] private int _failCount;

    private CancellationTokenSource? _filterCts;
    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);

    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private double _progressMax = 100;

    [ObservableProperty] private string _progressStatsText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _remainingTime = "";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private FileStatus? _selectedFilterStatus = null;
    [ObservableProperty] private FileSizeFilter _selectedSizeFilter = FileSizeFilter.All;
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private int _successCount;

    protected ImmutableList<FileItem> AllFiles = ImmutableList<FileItem>.Empty;
    protected readonly HashSet<string> ExistingPaths = new(StringComparer.OrdinalIgnoreCase);

    protected FileListViewModelBase()
    {
        Prefs.ForceCancelRequested += OnForceCancelRequested;
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                Localization = new LocalizationProxy(LocalizationService.Instance);
                OnPropertyChanged(nameof(TotalFilesText));
                UpdateStatsText();
            }
        };
    }

    protected LocalizationService L => LocalizationService.Instance;
    protected ConfigurationService ConfigService => ConfigurationService.Instance;
    protected PreferencesService Prefs => PreferencesService.Instance;
    protected LoggerService Logger => LoggerService.Instance;
    public bool HasFiles => AllFiles.Count > 0;

    public AvaloniaList<FileItem> Files { get; } = new();

    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());

    public List<FileStatus?> FilterStatusOptions { get; } = new()
    {
        null,
        FileStatus.Ready,
        FileStatus.Processing,
        FileStatus.Success,
        FileStatus.Failure,
        FileStatus.Cancelled
    };

    public List<FileSizeFilter> SizeFilterOptions { get; } = Enum.GetValues<FileSizeFilter>().ToList();

    public virtual string TotalFilesText => string.Format(L["Lbl_TotalFiles"], Files.Count);

    protected abstract bool IsGlobalBusy { get; }

    protected bool CanModifyList => !IsGlobalBusy;

    public void Dispose()
    {
        Prefs.ForceCancelRequested -= OnForceCancelRequested;
        foreach (var file in Files) file.Cts?.Dispose();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(true);
    partial void OnSelectedFilterStatusChanged(FileStatus? value) => ApplyFilter(true);
    partial void OnSelectedSizeFilterChanged(FileSizeFilter value) => ApplyFilter(true);

    protected async void ApplyFilter(bool debounce = false)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        if (debounce)
        {
            try
            {
                await Task.Delay(300, token); // Debounce
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (token.IsCancellationRequested) return;

        // Snapshot (atomic read of immutable list)
        var snapshot = AllFiles;
        var text = SearchText;
        var status = SelectedFilterStatus;
        var sizeFilter = SelectedSizeFilter;

        await Task.Run(async () =>
        {
            if (token.IsCancellationRequested) return;

            var filtered = snapshot.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(text))
            {
                filtered = filtered.Where(f => f.FileName.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            if (status.HasValue)
            {
                filtered = filtered.Where(f => f.ProcessingState == status.Value);
            }

            if (sizeFilter != FileSizeFilter.All)
            {
                long oneMB = AppConstants.OneMB;
                long oneHundredMB = 100L * AppConstants.OneMB;
                long oneGB = AppConstants.OneGB;

                filtered = filtered.Where(f =>
                {
                    var size = f.RawSizeBytes;
                    return sizeFilter switch
                    {
                        FileSizeFilter.Small => size < oneMB,
                        FileSizeFilter.Medium => size >= oneMB && size < oneHundredMB,
                        FileSizeFilter.Large => size >= oneHundredMB && size < oneGB,
                        FileSizeFilter.ExtraLarge => size >= oneGB,
                        _ => true
                    };
                });
            }

            var result = filtered.ToList();

            if (token.IsCancellationRequested) return;

            await RunOnUIAsync(() =>
            {
                if (token.IsCancellationRequested) return Task.CompletedTask;

                Files.Clear();
                Files.AddRange(result);
                OnPropertyChanged(nameof(TotalFilesText));
                NotifyCommands();
                return Task.CompletedTask;
            });
        }, token);
    }

    protected void AddItemsToAll(IEnumerable<FileItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        AllFiles = AllFiles.AddRange(list);
        foreach (var item in list) ExistingPaths.Add(item.FilePath);
        OnPropertyChanged(nameof(HasFiles));
        ApplyFilter(false);
    }

    private void OnForceCancelRequested(object? sender, EventArgs e)
    {
        Logger.Log($"Force Cancel requested by user ({GetType().Name}).", LogLevel.Warning);
        foreach (var file in Files) file.Cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual async Task ClearList()
    {
        foreach (var file in AllFiles)
        {
            file.Cts?.Cancel();
            file.Cts?.Dispose();
        }

        AllFiles = ImmutableList<FileItem>.Empty;
        ExistingPaths.Clear();
        Files.Clear();
        OnPropertyChanged(nameof(HasFiles));
        ProgressValue = 0;
        RemainingTime = "";
        OnPropertyChanged(nameof(TotalFilesText));
        NotifyCommands();
        Logger.Log(GetClearLogMessage());
    }

    protected virtual string GetClearLogMessage() => "Cleared file list.";

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    protected virtual void RemoveFile(FileItem? item)
    {
        if (item == null || item.IsProcessing) return;
        item.Cts?.Cancel();
        item.Cts?.Dispose();
        item.IsDeleted = true;

        AllFiles = AllFiles.Remove(item);
        ExistingPaths.Remove(item.FilePath);
        OnPropertyChanged(nameof(HasFiles));

        if (Files.Contains(item))
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(TotalFilesText));
            NotifyCommands();
            Logger.Log($"{GetRemoveLogPrefix()}: {item.FileName}");
        }
    }

    protected virtual string GetRemoveLogPrefix() => "Removed file";

    protected bool CanClearHash(FileItem? item)
    {
        return CanModifyList && item != null && item.ProcessingState != FileStatus.Ready;
    }

    [RelayCommand(CanExecute = nameof(CanClearHash))]
    protected virtual void ClearHash(FileItem? item)
    {
        if (item == null || item.IsProcessing) return;
        ResetItem(item);
        Logger.Log($"Cleared hash for {item.FileName}");
        NotifyCommands();
    }

    protected bool CanClearAllHashes()
    {
        return CanModifyList && Files.Any(f => f.ProcessingState != FileStatus.Ready);
    }

    [RelayCommand(CanExecute = nameof(CanClearAllHashes))]
    protected virtual void ClearAllHashes()
    {
        var count = 0;
        foreach (var item in Files)
        {
            if (!item.IsProcessing)
            {
                ResetItem(item);
                count++;
            }
        }
        if (count > 0)
        {
            Logger.Log($"Cleared hashes for {count} files.");
            NotifyCommands();
        }
    }

    protected virtual void ResetItem(FileItem item)
    {
        item.ResultHash = "";
        item.Status = L["Lbl_Status_Ready"];
        item.ProcessingState = FileStatus.Ready;
        item.IsMatch = null;
        item.IsCancelled = false;
        item.ProcessDuration = "";
    }

    public void UpdateStatsText()
    {
        var total = Files.Count;
        var percentage = total > 0 ? (double)ProcessedCount / total * 100 : 0;
        ProgressStatsText = string.Format(L["Lbl_ProgressStats"], ProcessedCount, total, percentage);

        var successPct = total > 0 ? (double)SuccessCount / total * 100 : 0;
        var failPct = total > 0 ? (double)FailCount / total * 100 : 0;
        var cancelPct = total > 0 ? (double)CancelledCount / total * 100 : 0;

        DetailedStatsText = string.Format(L["Lbl_DetailedStats"],
            SuccessCount, successPct,
            FailCount, failPct,
            CancelledCount, cancelPct);
    }

    public void UpdateSpeedText(double readBytesPerSecond, double writeBytesPerSecond = 0)
    {
        var readStr = FileItem.FormatSize((long)readBytesPerSecond);
        var writeStr = FileItem.FormatSize((long)writeBytesPerSecond);
        SpeedText = string.Format(L["Lbl_Speed"], readStr, writeStr);
    }

    // Abstract methods to hook into subclass specific logic
    protected abstract void NotifyCommands();

    [RelayCommand(CanExecute = nameof(IsGlobalBusy))]
    protected virtual void CancelAll()
    {
        Logger.Log("Cancel all requested.");
        _batchCts?.Cancel();
        foreach (var file in Files)
        {
            if (file.IsProcessing) file.Cts?.Cancel();
        }
    }

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
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], ex.Message, MessageBoxIcon.Error);
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
                AllowMultiple = true,
                Title = L["Dialog_SelectFolder"]
            });

            if (folders.Count == 0) return;

            if (Prefs.IsMaxFolderCountEnabled && folders.Count > Prefs.MaxFolderCount)
            {
                await MessageBoxHelper.ShowAsync(L["Msg_Error"],
                    string.Format(L["Msg_FolderLimit"], Prefs.MaxFolderCount), MessageBoxIcon.Error);
                return;
            }

            var allFiles = new List<string>();

            foreach (var folder in folders)
            {
                try
                {
                    var folderPath = folder.Path.LocalPath;
                    var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                    allFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error reading folder {folder.Path.LocalPath}: {ex.Message}", LogLevel.Error);
                }
            }

            if (allFiles.Count == 0)
            {
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_EmptyFolder"], MessageBoxIcon.Warning);
                Logger.Log("Selected folder(s) are empty.", LogLevel.Warning);
                return;
            }

            if (Prefs.IsMaxFileCountEnabled && allFiles.Count > Prefs.MaxFileCount)
            {
                var skippedCount = allFiles.Count - Prefs.MaxFileCount;
                var filesToAdd = allFiles.Take(Prefs.MaxFileCount).ToList();

                await AddFilesFromPaths(filesToAdd);

                await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
                    string.Format(L["Msg_FileLimit"], filesToAdd.Count, skippedCount), MessageBoxIcon.Warning);

                Logger.Log($"Added {filesToAdd.Count} files, skipped {skippedCount} due to limit.");
            }
            else
            {
                await AddFilesFromPaths(allFiles);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error adding folder: {ex.Message}", LogLevel.Error);
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], string.Format(L["Msg_OpenFolderError"], ex.Message), MessageBoxIcon.Error);
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
            ExistingPaths.Remove(item.FilePath);
        }

        AllFiles = AllFiles.RemoveRange(failedItems);

        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(TotalFilesText));
        NotifyCommands();
        Logger.Log(GetClearFailedLogMessage(failedItems.Count));

        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
            string.Format(L["Msg_ClearedFailed"], failedItems.Count), MessageBoxIcon.Information);
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
                await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
            }
        }
        catch
        {
            await action();
        }
    }
}