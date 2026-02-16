using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CheckHash.Models;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class CreateHashViewModel : FileListViewModelBase
{
    private readonly HashService _hashService = new();

    [ObservableProperty] private bool _isComputing;

    [ObservableProperty] private HashType _selectedAlgorithm = HashType.SHA256;

    protected override bool IsGlobalBusy => IsComputing;

    private bool CanComputeAll => Files.Count > 0 && !IsComputing;
    private bool CanCompress => Files.Count > 0 && !IsComputing;

    partial void OnIsComputingChanged(bool value)
    {
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        ComputeAllCommand.NotifyCanExecuteChanged();
        CancelAllCommand.NotifyCanExecuteChanged();
        CompressFilesCommand.NotifyCanExecuteChanged();

        // Base commands
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        ClearHashCommand.NotifyCanExecuteChanged();
        ClearAllHashesCommand.NotifyCanExecuteChanged();
        ClearFailedCommand.NotifyCanExecuteChanged();
        CopyToClipboardCommand.NotifyCanExecuteChanged();
        SaveHashFileCommand.NotifyCanExecuteChanged();
    }

    protected override IEnumerable<FileItem> GetFailedItems()
    {
        return Files.Where(f => string.IsNullOrEmpty(f.ResultHash));
    }

    public override async Task AddFilesFromPaths(IEnumerable<string> filePaths)
    {
        var config = await ConfigService.LoadAsync();
        long limitBytes = 0;
        if (config.IsFileSizeLimitEnabled) limitBytes = Prefs.GetMaxSizeBytes();

        var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath));

        IsComputing = true;
        var selectedAlgo = SelectedAlgorithm;
        try
        {
            await Task.Run(async () =>
            {
                var uniquePathsToProcess = new List<string>();
                foreach (var path in filePaths)
                {
                    if (existingPaths.Contains(path)) continue;
                    existingPaths.Add(path);
                    uniquePathsToProcess.Add(path);
                }

                if (uniquePathsToProcess.Count == 0) return;

                var inputs = uniquePathsToProcess.ToArray();
                var results = new FileItem?[inputs.Length];
                var skippedFiles = new ConcurrentQueue<string>();

                Parallel.For(0, inputs.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                    {
                        var path = inputs[i];
                        var info = new FileInfo(path);
                        var len = info.Length;
                        var fileName = Path.GetFileName(path);

                        if (config.IsFileSizeLimitEnabled && len > limitBytes)
                        {
                            var msg = string.Format(L["Msg_FileSizeLimitExceeded"], fileName, config.FileSizeLimitValue,
                                config.FileSizeLimitUnit);
                            Logger.Log(msg, LogLevel.Warning);
                            skippedFiles.Enqueue(fileName);
                            return;
                        }

                        var item = new FileItem
                        {
                            FileName = fileName,
                            FilePath = path,
                            FileSize = FileItem.FormatSize(len),
                            RawSizeBytes = len,
                            SelectedAlgorithm = selectedAlgo
                        };
                        results[i] = item;
                        Logger.Log($"Added file: {fileName}");
                    });

                var newItems = new List<FileItem>(inputs.Length);
                foreach (var res in results)
                {
                    if (res != null) newItems.Add(res);
                }

                if (newItems.Count > 0)
                    await Dispatcher.UIThread.InvokeAsync(() => { AddItemsToAll(newItems); });

                if (!skippedFiles.IsEmpty)
                {
                    var fileList = string.Join("\n", skippedFiles.Take(10));
                    if (skippedFiles.Count > 10) fileList += "\n...";

                    var summaryMsg = string.Format(L["Msg_FileSizeLimitExceeded_Summary"], skippedFiles.Count,
                        config.FileSizeLimitValue, config.FileSizeLimitUnit, fileList);

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                        await MessageBoxHelper.ShowAsync(L["Msg_Error"], summaryMsg, MessageBoxIcon.Error));
                }
            });
        }
        finally
        {
            IsComputing = false;
            OnPropertyChanged(nameof(TotalFilesText));
            ComputeAllCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompress))]
    private async Task CompressFiles(Window window)
    {
        if (Files.Count == 0) return;

        var fileSave = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["Dialog_SaveZip"],
            SuggestedFileName = $"Archive_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(L["Dialog_FileType_Zip"])
                {
                    Patterns = new[] { "*.zip" },
                    MimeTypes = new[] { "application/zip" }
                }
            }
        });

        if (fileSave != null)
            try
            {
                var zipPath = fileSave.Path.LocalPath;
                Logger.Log($"Compressing files to: {zipPath}");
                var filesToCompress = Files.ToList();


                await Task.Run(async () =>
                {
                    await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        4096, useAsync: true);
                    await using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
                    foreach (var item in filesToCompress)
                        if (!string.IsNullOrEmpty(item.ResultHash))
                        {
                            var ext = item.SelectedAlgorithm.ToString().ToLower();
                            var hashFileName = $"{item.FileName}.{ext}";

                            var entry = archive.CreateEntry(hashFileName);
                            await using var entryStream = entry.Open();
                            await using var writer = new StreamWriter(entryStream);
                            await writer.WriteAsync(item.ResultHash);
                        }
                });

                foreach (var f in Files) f.Status = L["Status_Compressed"];
                Logger.Log("Compression successful.", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Logger.Log($"Compression failed: {ex.Message}", LogLevel.Error);
            }
    }

    [RelayCommand(CanExecute = nameof(CanComputeAll))]
    private async Task ComputeAll()
    {
        Logger.Log("Starting batch computation...");
        IsComputing = true;
        ProgressMax = Files.Count;
        ProgressValue = 0;
        RemainingTime = L["Msg_TimeUnknown"];

        // Reset Stats
        ProcessedCount = 0;
        SuccessCount = 0;
        FailCount = 0;
        CancelledCount = 0;
        SpeedText = "";
        UpdateStatsText();

        var success = 0;
        var fail = 0;
        var cancelled = 0;
        var processedCounter = 0;

        long totalBytesRead = 0;
        long totalBytesWritten = 0;
        long lastBytesRead = 0;
        long lastBytesWritten = 0;
        var lastSpeedUpdate = DateTime.UtcNow;
        var showSpeed = (await ConfigService.LoadAsync()).ShowReadWriteSpeed;

        var queue = Files.ToList();
        var startTime = DateTime.UtcNow;

        var statusDone = L["Status_Done"];
        var statusCancelled = L["Status_Cancelled"];

        // Reset items
        foreach (var item in queue)
        {
            item.ResultHash = "";
            item.Status = L["Status_Computing"];
            item.ProcessingState = FileStatus.Ready;
            item.IsMatch = null;
            item.IsCancelled = false;
            item.ProcessDuration = "";
        }

        _batchCts = new CancellationTokenSource();
        using var progressCts = new CancellationTokenSource();
        var progressTask = Task.Run(async () =>
        {
            try
            {
                while (!progressCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, progressCts.Token);
                    var current = processedCounter;

                    var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    var rate = current / elapsed;
                    var remainingCount = queue.Count - current;
                    var eta = rate > 0 ? TimeSpan.FromSeconds(remainingCount / rate) : TimeSpan.Zero;
                    var etaStr = current > 0 && current < queue.Count
                        ? string.Format(L["Msg_EstimatedTime"], $"{eta.Minutes:D2}:{eta.Seconds:D2}")
                        : L["Msg_TimeUnknown"];

                    // Speed Calculation
                    if (showSpeed)
                    {
                        var now = DateTime.UtcNow;
                        var currentTotalRead = Interlocked.Read(ref totalBytesRead);
                        var currentTotalWrite = Interlocked.Read(ref totalBytesWritten);

                        var diffRead = currentTotalRead - lastBytesRead;
                        var diffWrite = currentTotalWrite - lastBytesWritten;

                        var timeDiff = (now - lastSpeedUpdate).TotalSeconds;

                        if (timeDiff > 0.5) // Update every 0.5s to avoid jitter
                        {
                            var readBytesPerSec = diffRead / timeDiff;
                            var writeBytesPerSec = diffWrite / timeDiff;

                            await Dispatcher.UIThread.InvokeAsync(() =>
                                UpdateSpeedText(readBytesPerSec, writeBytesPerSec));

                            lastBytesRead = currentTotalRead;
                            lastBytesWritten = currentTotalWrite;
                            lastSpeedUpdate = now;
                        }
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ProgressValue = current;
                        RemainingTime = etaStr;

                        ProcessedCount = current;
                        SuccessCount = success;
                        FailCount = fail;
                        CancelledCount = cancelled;
                        UpdateStatsText();
                    });

                    if (current >= queue.Count) break;
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            var strategyService = new ProcessingStrategyService();
            var batches = strategyService.GetProcessingBatches(queue, f => f.RawSizeBytes);
            Logger.Log($"Starting processing with 3 streams. Small: {batches[0].Count}, Medium: {batches[1].Count}, Large: {batches[2].Count}");

            Action<long>? progressCallback = null;
            if (showSpeed)
            {
                progressCallback = (bytes) => Interlocked.Add(ref totalBytesRead, bytes);
            }

            var tasks = new List<Task>();

            foreach (var batch in batches)
            {
                if (batch.Count == 0) continue;

                tasks.Add(Task.Run(async () =>
                {
                    foreach (var file in batch)
                    {
                        if (_batchCts?.Token.IsCancellationRequested == true) break;

                        if (file.IsDeleted)
                        {
                            Interlocked.Increment(ref processedCounter);
                            continue;
                        }

                        var bufferSize = strategyService.GetBufferSize(file.RawSizeBytes, file.SelectedAlgorithm);
                        await ProcessItemAsync(file, bufferSize, progressCallback);

                        Interlocked.Increment(ref processedCounter);

                        if (file.Status == statusDone) Interlocked.Increment(ref success);
                        else if (file.Status == statusCancelled) Interlocked.Increment(ref cancelled);
                        else Interlocked.Increment(ref fail);
                    }
                }, _batchCts?.Token ?? CancellationToken.None));
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Batch computation cancelled.");
        }
        finally
        {
            progressCts.Cancel();
            _batchCts?.Dispose();
            _batchCts = null;
        }

        var cancelledStatus = L["Status_Cancelled"];
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var item in queue)
            {
                if (item.ProcessingState == FileStatus.Ready)
                {
                    item.Status = cancelledStatus;
                    item.ProcessingState = FileStatus.Cancelled;
                    item.IsCancelled = true;
                    cancelled++;
                    processedCounter++;
                }
            }
        });

        try
        {
            await progressTask;
        }
        catch
        {
            // Ignore cancellation/tasks errors
        }

        ProgressValue = processedCounter;
        RemainingTime = "";
        IsComputing = false;

        ProcessedCount = processedCounter;
        SuccessCount = success;
        FailCount = fail;
        CancelledCount = cancelled;
        UpdateStatsText();
        SpeedText = "";

        Logger.Log($"Batch finished. Success: {success}, Failed: {fail}, Cancelled: {cancelled}");

        var icon = (fail > 0 || cancelled > 0) ? MessageBoxIcon.Warning : MessageBoxIcon.Success;
        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
            string.Format(L["Msg_Result_Content"], success, fail, cancelled), icon);
    }

    private bool CanSaveHashFile(FileItem? item)
    {
        return !IsComputing && item != null && !string.IsNullOrEmpty(item.ResultHash);
    }

    [RelayCommand(CanExecute = nameof(CanSaveHashFile))]
    private async Task SaveHashFile(FileItem item)
    {
        if (string.IsNullOrEmpty(item.ResultHash)) return;

        var ext = item.SelectedAlgorithm.ToString().ToLower();

        var window =
            Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["Dialog_SaveHash"],
            SuggestedFileName = $"{item.FileName}.{ext}",
            DefaultExtension = ext
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(item.ResultHash);
            item.Status = L["Status_Saved"];
            Logger.Log($"Saved hash file for {item.FileName}", LogLevel.Success);
        }
    }

    private bool CanCopyToClipboard(string? hash)
    {
        return !IsComputing && !string.IsNullOrEmpty(hash);
    }

    [RelayCommand(CanExecute = nameof(CanCopyToClipboard))]
    private async Task CopyToClipboard(string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(hash);
                Logger.Log("Hash copied to clipboard.");
            }
        }
    }

    private async Task ProcessItemAsync(FileItem item, int? bufferSize = null, Action<long>? progressCallback = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            item.IsProcessing = true;
            item.ProcessingState = FileStatus.Processing;
            item.Status = string.Format(L["Status_Processing"], item.SelectedAlgorithm);
            item.ProcessDuration = "";
        });

        item.Cts = new CancellationTokenSource();

        if (Prefs.IsFileTimeoutEnabled) item.Cts.CancelAfter(TimeSpan.FromSeconds(Prefs.FileTimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        Logger.Log($"Computing hash for {item.FileName} ({item.SelectedAlgorithm})...");
        if (item.SelectedAlgorithm.IsInsecure())
        {
            LoggerService.Instance.Log($"Weak algorithm selected: {item.SelectedAlgorithm}", LogLevel.Warning);
        }

        string? resultHash = null;
        string status = "";

        try
        {
            resultHash = await _hashService.ComputeHashAsync(item.FilePath, item.SelectedAlgorithm, item.Cts.Token,
                bufferSize, progressCallback);
            status = L["Status_Done"];
            await Dispatcher.UIThread.InvokeAsync(() => item.ProcessingState = FileStatus.Success);
            Logger.Log($"Computed {item.FileName}: {resultHash}", LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            status = L["Status_Cancelled"];
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.IsCancelled = true;
                item.ProcessingState = FileStatus.Cancelled;
            });
            Logger.Log($"Cancelled computation for {item.FileName}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            status = string.Format(L["Status_Error"], ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() => item.ProcessingState = FileStatus.Failure);
            Logger.Log($"Error computing {item.FileName}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            stopwatch.Stop();
            var duration = stopwatch.Elapsed.TotalSeconds < 1
                ? $"{stopwatch.ElapsedMilliseconds}ms"
                : $"{stopwatch.Elapsed.TotalSeconds:F2}s";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (resultHash != null) item.ResultHash = resultHash;
                item.Status = status;
                item.ProcessDuration = duration;
                item.IsProcessing = false;
            });

            item.Cts?.Dispose();
            item.Cts = null;
        }
    }

    [RelayCommand]
    private async Task ComputeSingle(FileItem item)
    {
        if (IsComputing && !item.IsProcessing) return;
        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }

        try
        {
            IsComputing = true;
            item.ResultHash = "";
            item.Status = L["Status_Computing"];
            item.ProcessingState = FileStatus.Ready;
            item.IsCancelled = false;
            await ProcessItemAsync(item);
        }
        finally
        {
            IsComputing = false;
        }
    }
}