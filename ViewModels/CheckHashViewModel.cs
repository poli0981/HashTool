using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CheckHash.Models;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class CheckHashViewModel : FileListViewModelBase
{
    private const long MaxHashFileSize = AppConstants.OneMB; // 1MB
    private readonly HashService _hashService = new();

    [ObservableProperty] private HashType _globalAlgorithm = HashType.SHA256;

    [ObservableProperty] private bool _isChecking;

    protected override bool IsGlobalBusy => IsChecking;

    private bool CanVerify => Files.Count > 0 && !IsChecking;

    partial void OnIsCheckingChanged(bool value)
    {
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        VerifyAllCommand.NotifyCanExecuteChanged();
        CancelAllCommand.NotifyCanExecuteChanged();

        // Base commands
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ClearListCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        ClearFailedCommand.NotifyCanExecuteChanged();
        BrowseHashFileCommand.NotifyCanExecuteChanged();
    }

    protected override IEnumerable<FileItem> GetFailedItems()
    {
        return Files.Where(f => f.IsMatch != true);
    }

    public override async Task AddFilesFromPaths(IEnumerable<string> filePaths)
    {
        IsChecking = true;
        try
        {
            var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath));

            await Task.Run(async () =>
            {
                var config = await ConfigService.LoadAsync();
                long limitBytes = 0;
                if (config.IsFileSizeLimitEnabled) limitBytes = Prefs.GetMaxSizeBytes();

                var pathList = filePaths.ToList();
                var results = new FilePreparationResult[pathList.Count];

                await Parallel.ForEachAsync(Enumerable.Range(0, pathList.Count), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (i, ct) =>
                {
                    var path = pathList[i];
                    var result = new FilePreparationResult { Path = path };

                    try
                    {
                        var info = new FileInfo(path);
                        result.Info = info;

                        if (config.IsFileSizeLimitEnabled && info.Length > limitBytes)
                        {
                            result.Status = FilePreparationStatus.FileTooLarge;
                            results[i] = result;
                            return;
                        }

                        var ext = Path.GetExtension(path).TrimStart('.').ToUpper();
                        var isHashFile = Enum.TryParse<HashType>(ext, true, out var detectedAlgo);

                        result.IsHashFile = isHashFile;
                        result.DetectedAlgo = detectedAlgo;

                        if (isHashFile)
                        {
                            var dir = Path.GetDirectoryName(path);
                            if (dir != null)
                            {
                                var sourcePath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path));
                                result.SourcePath = sourcePath;

                                var sourceInfo = new FileInfo(sourcePath);
                                if (sourceInfo.Exists)
                                {
                                    result.SourceExists = true;
                                    result.SourceInfo = sourceInfo;

                                    if (config.IsFileSizeLimitEnabled && sourceInfo.Length > limitBytes)
                                    {
                                        result.Status = FilePreparationStatus.SourceTooLarge;
                                        results[i] = result;
                                        return;
                                    }
                                }

                                result.HashContent = await ReadHashFromFileAsync(path);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors during preparation
                    }

                    results[i] = result;
                });

                var newItems = new List<FileItem>();
                var skippedFiles = new List<string>();

                foreach (var result in results)
                {
                    if (result == null) continue;

                    var path = result.Path;
                    var fileName = Path.GetFileName(path);

                    if (result.Status == FilePreparationStatus.FileTooLarge)
                    {
                        var msg = string.Format(L["Msg_FileSizeLimitExceeded"], fileName, config.FileSizeLimitValue,
                            config.FileSizeLimitUnit);
                        Logger.Log(msg, LogLevel.Warning);
                        skippedFiles.Add(fileName);
                        continue;
                    }

                    if (result.Status == FilePreparationStatus.SourceTooLarge)
                    {
                        var sourceName = Path.GetFileName(result.SourcePath);
                        var msg = string.Format(L["Msg_FileSizeLimitExceeded"], sourceName,
                            config.FileSizeLimitValue, config.FileSizeLimitUnit);
                        Logger.Log(msg, LogLevel.Warning);
                        skippedFiles.Add(sourceName);
                        continue;
                    }

                    if (result.IsHashFile)
                    {
                        if (string.IsNullOrEmpty(result.SourcePath)) continue;

                        var item = new FileItem
                        {
                            FileName = Path.GetFileName(result.SourcePath),
                            FilePath = result.SourcePath,
                            ExpectedHash = result.HashContent,
                            Status = result.SourceExists ? L["Status_ReadyFromHash"] : L["Status_MissingOriginal"],
                            SelectedAlgorithm = result.DetectedAlgo,
                            HasSpecificAlgorithm = true
                        };

                        if (result.SourceExists && result.SourceInfo != null)
                        {
                            item.FileSize = FileItem.FormatSize(result.SourceInfo.Length);
                            item.RawSizeBytes = result.SourceInfo.Length;
                        }
                        else
                        {
                            item.IsMatch = false;
                        }

                        newItems.Add(item);
                        existingPaths.Add(item.FilePath);
                        Logger.Log($"Added check item (from hash file): {item.FileName}");
                    }
                    else
                    {
                        if (!existingPaths.Contains(path) && result.Info != null)
                        {
                            var item = new FileItem
                            {
                                FileName = fileName,
                                FilePath = path,
                                RawSizeBytes = result.Info.Length,
                                FileSize = FileItem.FormatSize(result.Info.Length),
                                Status = L["Status_Waiting"],
                                ExpectedHash = ""
                            };

                            newItems.Add(item);
                            existingPaths.Add(path);
                            Logger.Log($"Added check item: {fileName}");
                        }
                    }
                }

                if (newItems.Count > 0)
                {
                    await RunOnUIAsync(() =>
                    {
                        AddItemsToAll(newItems);
                        return Task.CompletedTask;
                    });
                }

                if (skippedFiles.Count > 0)
                {
                    var fileList = string.Join("\n", skippedFiles.Take(10));
                    if (skippedFiles.Count > 10) fileList += "\n...";

                    var summaryMsg = string.Format(L["Msg_FileSizeLimitExceeded_Summary"], skippedFiles.Count,
                        config.FileSizeLimitValue, config.FileSizeLimitUnit, fileList);

                    await RunOnUIAsync(async () => await MessageBoxHelper.ShowAsync(L["Msg_Error"], summaryMsg, MessageBoxIcon.Error));
                }
            });

            await RunOnUIAsync(() =>
            {
                OnPropertyChanged(nameof(TotalFilesText));
                VerifyAllCommand.NotifyCanExecuteChanged();
                return Task.CompletedTask;
            });
        }
        finally
        {
            IsChecking = false;
        }
    }


    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAllAsync()
    {
        Logger.Log("Starting batch verification...");
        IsChecking = true;
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

        var queue = Files.ToList();
        var processedCounter = 0;
        var cancelled = 0;
        var match = 0;
        var mismatch = 0;

        long totalBytesRead = 0;
        long totalBytesWritten = 0;
        long lastBytesRead = 0;
        long lastBytesWritten = 0;
        var lastSpeedUpdate = DateTime.UtcNow;
        var showSpeed = (await ConfigService.LoadAsync()).ShowReadWriteSpeed;

        var startTime = DateTime.UtcNow;

        // Reset items
        foreach (var item in queue)
        {
            item.Status = L["Status_Waiting"];
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
                        ? string.Format(L["Msg_EstimatedTime"], $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}")
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

                        if (timeDiff > 0.5) // Update every 0.5s
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
                        SuccessCount = match;
                        FailCount = mismatch;
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

        var statusCancelled = L["Status_Cancelled"];

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

                        var algo = file.HasSpecificAlgorithm ? file.SelectedAlgorithm : GlobalAlgorithm;
                        var bufferSize = strategyService.GetBufferSize(file.RawSizeBytes, algo);
                        await VerifyItemLogicAsync(file, bufferSize, progressCallback);

                        Interlocked.Increment(ref processedCounter);

                        if (file.IsMatch == true) Interlocked.Increment(ref match);
                        else if (file.IsMatch == false) Interlocked.Increment(ref mismatch);

                        if (file.Status == statusCancelled)
                        {
                            Interlocked.Increment(ref cancelled);
                        }
                    }
                }, _batchCts?.Token ?? CancellationToken.None));
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Batch verification cancelled.");
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
        IsChecking = false;

        ProcessedCount = processedCounter;
        SuccessCount = match;
        FailCount = mismatch;
        CancelledCount = cancelled;
        UpdateStatsText();
        SpeedText = "";

        var totalDuration = DateTime.UtcNow - startTime;
        var durationStr = $"{(int)totalDuration.TotalHours}:{totalDuration.Minutes:D2}:{totalDuration.Seconds:D2}";
        Logger.Log($"Batch verification finished in {durationStr}. Match: {match}, Mismatch/Error: {mismatch}, Cancelled: {cancelled}");

        if (cancelled > 0)
        {
            var msg = L["Msg_TaskCancelled_Content"];
            msg += $"\n{string.Format(L["Msg_TaskDuration"], durationStr)}";
            Logger.Log(msg, LogLevel.Warning);
            await MessageBoxHelper.ShowAsync(L["Msg_TaskCancelled_Title"], msg, MessageBoxIcon.Warning);
        }
        else
        {
            var icon = mismatch > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Success;
            var resultMsg = string.Format(L["Msg_CheckResult"], Files.Count, match, mismatch, cancelled);
            resultMsg += $"\n{string.Format(L["Msg_TaskDuration"], durationStr)}";
            await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"], resultMsg, icon);
        }
    }

    private async Task<string> ReadHashFromFileAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxHashFileSize)
            {
                Logger.Log($"Hash file too large (>{MaxHashFileSize / 1024}KB): {path}", LogLevel.Warning);
                return "";
            }

            var regex = HashRegex();

            var content = await File.ReadAllTextAsync(path);
            var match = regex.Match(content);
            if (match.Success) return match.Value;
            return content.Trim();
        }
        catch
        {
            return "";
        }
    }

    [GeneratedRegex(@"[a-fA-F0-9]{8,128}")]
    private static partial Regex HashRegex();


    [RelayCommand]
    private async Task VerifySingleAsync(FileItem item)
    {
        if (IsChecking && !item.IsProcessing) return;

        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }

        try
        {
            IsChecking = true;
            item.Status = L["Status_Waiting"];
            item.IsMatch = null;
            item.IsCancelled = false;
            await VerifyItemLogicAsync(item);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task VerifyItemLogicAsync(FileItem file, int? bufferSize = null,
        Action<long>? progressCallback = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            file.IsProcessing = true;
            file.ProcessingState = FileStatus.Processing;
            file.IsMatch = null;
            file.ProcessDuration = "";
            file.Status = L["Status_Waiting"];
        });

        file.Cts = new CancellationTokenSource();
        if (Prefs.IsFileTimeoutEnabled) file.Cts.CancelAfter(TimeSpan.FromSeconds(Prefs.FileTimeoutSeconds));

        await Task.Run(async () =>
        {
            var result = await VerifyFileInternalAsync(file, file.Cts.Token, bufferSize, progressCallback);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.NewExpectedHash != null) file.ExpectedHash = result.NewExpectedHash;

                file.Status = result.Status;
                file.IsMatch = result.IsMatch;
                file.ProcessDuration = result.Duration;
                file.IsProcessing = false;

                if (file.IsCancelled)
                    file.ProcessingState = FileStatus.Cancelled;
                else if (result.IsMatch == true)
                    file.ProcessingState = FileStatus.Success;
                else
                    file.ProcessingState = FileStatus.Failure;

                file.Cts?.Dispose();
                file.Cts = null;
            });
        });
    }

    private async Task<VerificationResult> VerifyFileInternalAsync(FileItem file, CancellationToken ct,
        int? bufferSize = null, Action<long>? progressCallback = null)
    {
        var sw = Stopwatch.StartNew();
        var result = new VerificationResult
        {
            // Default failure state in case of exception
            Status = L["Status_Invalid"],
            IsMatch = false
        };

        Logger.Log($"Verifying {file.FileName}...");

        try
        {
            if (!File.Exists(file.FilePath))
            {
                result.Status = L["Status_LostOriginal"];
                result.IsMatch = false;
                Logger.Log($"Original file missing: {file.FileName}", LogLevel.Error);
                return result;
            }

            var expectedHash = file.ExpectedHash;

            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                var globalExt = GlobalAlgorithm.ToString().ToLower();
                var sidecarPath = $"{file.FilePath}.{globalExt}";

                if (File.Exists(sidecarPath))
                {
                    var hash = await ReadHashFromFileAsync(sidecarPath);
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        expectedHash = hash;
                        result.NewExpectedHash = hash;
                        Logger.Log($"Found sidecar hash for {file.FileName}");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                result.Status = L["Status_MissingHash"];
                result.IsMatch = false;
                Logger.Log($"Missing expected hash for {file.FileName}", LogLevel.Warning);
            }
            else
            {
                var algoToCheck = file.HasSpecificAlgorithm ? file.SelectedAlgorithm : GlobalAlgorithm;
                if (algoToCheck.IsInsecure())
                {
                    LoggerService.Instance.Log($"Weak algorithm used for verification: {algoToCheck}",
                        LogLevel.Warning);
                }

                var actualHash =
                    await _hashService.ComputeHashAsync(file.FilePath, algoToCheck, ct, bufferSize, progressCallback);

                if (string.Equals(actualHash, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = L["Status_Valid"];
                    result.IsMatch = true;
                    Logger.Log($"Verification VALID: {file.FileName}", LogLevel.Success);
                }
                else
                {
                    result.Status = L["Status_Invalid"];
                    result.IsMatch = false;
                    Logger.Log(
                        $"Verification INVALID: {file.FileName}. Expected: {expectedHash}, Actual: {actualHash}",
                        LogLevel.Error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = L["Status_Cancelled"];
            result.IsMatch = null;
            await Dispatcher.UIThread.InvokeAsync(() => file.IsCancelled = true);
            Logger.Log($"Verification cancelled: {file.FileName}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            result.Status = ex.Message;
            result.IsMatch = false;
            Logger.Log($"Verification error {file.FileName}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            sw.Stop();
            var duration = sw.Elapsed.TotalSeconds < 1
                ? $"{sw.ElapsedMilliseconds}ms"
                : $"{sw.Elapsed.TotalSeconds:F2}s";
            result.Duration = duration;
        }

        return result;
    }

    private bool CanBrowseHashFile(FileItem item)
    {
        return !IsChecking;
    }

    [RelayCommand(CanExecute = nameof(CanBrowseHashFile))]
    private async Task BrowseHashFileAsync(FileItem item)
    {
        var window =
            Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        if (window == null) return;

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = string.Format(L["Dialog_SelectHashFile"], item.FileName),
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var hashFilePath = result[0].Path.LocalPath;
            var hashFileName = Path.GetFileName(hashFilePath);

            if (!hashFileName.Contains(item.FileName, StringComparison.OrdinalIgnoreCase))
            {
                await MessageBoxHelper.ShowAsync(L["Msg_WrongHashFile"],
                    string.Format(L["Msg_WrongHashFileContent"], item.FileName, hashFileName), MessageBoxIcon.Warning);
                return;
            }

            try
            {
                item.ExpectedHash = await ReadHashFromFileAsync(hashFilePath);
                item.Status = L["Status_HashFileLoaded"];
                Logger.Log($"Loaded hash file for {item.FileName}");
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_ReadHashError"], MessageBoxIcon.Error);
                Logger.Log($"Error reading hash file: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private record struct VerificationResult(
        string Status,
        bool? IsMatch,
        string Duration,
        string? NewExpectedHash = null
    );

    private enum FilePreparationStatus
    {
        Success,
        FileTooLarge,
        SourceTooLarge
    }

    private sealed class FilePreparationResult
    {
        public required string Path { get; init; }
        public FilePreparationStatus Status { get; set; } = FilePreparationStatus.Success;
        public FileInfo? Info { get; set; }
        public bool IsHashFile { get; set; }
        public HashType DetectedAlgo { get; set; }
        public string SourcePath { get; set; } = "";
        public bool SourceExists { get; set; }
        public FileInfo? SourceInfo { get; set; }
        public string HashContent { get; set; } = "";
    }
}