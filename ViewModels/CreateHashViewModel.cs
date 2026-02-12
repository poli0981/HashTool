using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Collections;
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
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckHash.Models;

namespace CheckHash.ViewModels;

public partial class CreateHashViewModel : FileListViewModelBase
{
    private readonly HashService _hashService = new();

    [ObservableProperty]
    private bool _isComputing;

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
        CompressFilesCommand.NotifyCanExecuteChanged();

        // Base commands
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        ClearFailedCommand.NotifyCanExecuteChanged();
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
                    await Dispatcher.UIThread.InvokeAsync(() => { Files.AddRange(newItems); });

                if (!skippedFiles.IsEmpty)
                {
                    var fileList = string.Join("\n", skippedFiles.Take(10));
                    if (skippedFiles.Count > 10) fileList += "\n...";

                    var summaryMsg = string.Format(L["Msg_FileSizeLimitExceeded_Summary"], skippedFiles.Count,
                        config.FileSizeLimitValue, config.FileSizeLimitUnit, fileList);

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                        await MessageBoxHelper.ShowAsync(L["Msg_Error"], summaryMsg));
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

        var success = 0;
        var fail = 0;
        var cancelled = 0;
        var queue = Files.ToList();
        var processedCount = 0;
        var startTime = DateTime.UtcNow;

        var statusDone = L["Status_Done"];
        var statusCancelled = L["Status_Cancelled"];

        using var cts = new CancellationTokenSource();
        var progressTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, cts.Token);
                    var current = processedCount;

                    var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    var rate = current / elapsed;
                    var remainingCount = queue.Count - current;
                    var eta = rate > 0 ? TimeSpan.FromSeconds(remainingCount / rate) : TimeSpan.Zero;
                    var etaStr = current > 0 && current < queue.Count
                        ? string.Format(L["Msg_EstimatedTime"], $"{eta.Minutes:D2}:{eta.Seconds:D2}")
                        : L["Msg_TimeUnknown"];

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ProgressValue = current;
                        RemainingTime = etaStr;
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
            await Parallel.ForEachAsync(queue, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (file, ct) =>
            {
                if (file.IsDeleted)
                {
                    Interlocked.Increment(ref processedCount);
                    return;
                }

                await ProcessItemAsync(file);

                Interlocked.Increment(ref processedCount);

                if (file.Status == statusDone) Interlocked.Increment(ref success);
                else if (file.Status == statusCancelled) Interlocked.Increment(ref cancelled);
                else Interlocked.Increment(ref fail);
            });
        }
        finally
        {
            cts.Cancel();
        }

        try
        {
            await progressTask;
        }
        catch
        {
            // Ignore cancellation/tasks errors
        }

        ProgressValue = processedCount;
        RemainingTime = "";
        IsComputing = false;
        Logger.Log($"Batch finished. Success: {success}, Failed: {fail}, Cancelled: {cancelled}");

        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
            string.Format(L["Msg_Result_Content"], success, fail, cancelled));
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task CopyToClipboard(string hash)
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

    private async Task ProcessItemAsync(FileItem item)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            item.IsProcessing = true;
            item.Status = string.Format(L["Status_Processing"], item.SelectedAlgorithm);
            item.ProcessDuration = "";
        });

        item.Cts = new CancellationTokenSource();

        if (Prefs.IsFileTimeoutEnabled) item.Cts.CancelAfter(TimeSpan.FromSeconds(Prefs.FileTimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        Logger.Log($"Computing hash for {item.FileName} ({item.SelectedAlgorithm})...");

        string? resultHash = null;
        string status = "";

        try
        {
            resultHash = await _hashService.ComputeHashAsync(item.FilePath, item.SelectedAlgorithm, item.Cts.Token);
            status = L["Status_Done"];
            Logger.Log($"Computed {item.FileName}: {resultHash}", LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            status = L["Status_Cancelled"];
            await Dispatcher.UIThread.InvokeAsync(() => item.IsCancelled = true);
            Logger.Log($"Cancelled computation for {item.FileName}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            status = string.Format(L["Status_Error"], ex.Message);
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
        if (item.IsCancelled) return;
        if (IsComputing && !item.IsProcessing) return;
        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }

        try
        {
            IsComputing = true;
            await ProcessItemAsync(item);
        }
        finally
        {
            IsComputing = false;
        }
    }
}
