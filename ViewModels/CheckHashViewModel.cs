using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

public partial class CheckHashViewModel : ObservableObject, IDisposable
{
    private const long MaxHashFileSize = AppConstants.OneMB; // 1MB
    private readonly HashService _hashService = new();
    [ObservableProperty] private HashType _globalAlgorithm = HashType.SHA256;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesToCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearListCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFileCommand))]
    private bool _isChecking;

    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);
    [ObservableProperty] private double _progressMax = 100;

    [ObservableProperty] private double _progressValue;

    public CheckHashViewModel()
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

    private LocalizationService L => LocalizationService.Instance;
    private ConfigurationService ConfigService => ConfigurationService.Instance;
    private PreferencesService Prefs => PreferencesService.Instance;
    private LoggerService Logger => LoggerService.Instance;

    public AvaloniaList<FileItem> Files { get; } = new();

    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());

    public string TotalFilesText => string.Format(L["Lbl_TotalFiles"], Files.Count);

    private bool CanModifyList => !IsChecking;

    private bool CanVerify => Files.Count > 0 && !IsChecking;

    public void Dispose()
    {
        Prefs.ForceCancelRequested -= OnForceCancelRequested;
        foreach (var file in Files) file.Cts?.Dispose();
    }

    private void OnForceCancelRequested(object? sender, EventArgs e)
    {
        Logger.Log("Force Cancel requested by user (Check Hash).", LogLevel.Warning);
        foreach (var file in Files) file.Cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void ClearList()
    {
        foreach (var file in Files)
        {
            file.Cts?.Cancel();
            file.Cts?.Dispose();
        }

        Files.Clear();
        ProgressValue = 0;
        OnPropertyChanged(nameof(TotalFilesText));
        VerifyAllCommand.NotifyCanExecuteChanged();
        Logger.Log("Cleared check list.");
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
       private async Task VerifyAllAsync()
    {
        Logger.Log("Starting batch verification...");
        IsChecking = true;
        ProgressMax = Files.Count;
        ProgressValue = 0;
        var queue = Files.ToList();
        var counters = new int[2];

        using var cts = new CancellationTokenSource();
        var progressTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, cts.Token);
                    var current = counters[0];
                    await Dispatcher.UIThread.InvokeAsync(() => ProgressValue = current);
                    if (current >= queue.Count) break;
                }
            }
            catch (OperationCanceledException) { }
        });

        var statusCancelled = L["Status_Cancelled"];

        try
        {
            await Parallel.ForEachAsync(queue, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (file, ct) =>
            {
                await VerifyItemLogicAsync(file);

                Interlocked.Increment(ref counters[0]);
                if (file.Status == statusCancelled)
                {
                    Interlocked.Increment(ref counters[1]);
                }
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

        ProgressValue = counters[0];
        IsChecking = false;

        var cancelled = counters[1];

        var match = 0;
        var failCount = 0;
        foreach (var f in Files)
        {
            if (f.IsMatch == true) match++;
            else if (f.IsMatch == false) failCount++;
        }

        Logger.Log($"Batch verification finished. Match: {match}, Mismatch/Error: {failCount}, Cancelled: {cancelled}");

        if (cancelled > 0)
        {
            var msg = L["Msg_TaskCancelled_Content"];
            Logger.Log(msg, LogLevel.Warning);
            await MessageBoxHelper.ShowAsync(L["Msg_TaskCancelled_Title"], msg);
        }
        else
        {
            await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
                string.Format(L["Msg_CheckResult"], Files.Count, match, failCount, cancelled));
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private async Task AddFilesToCheckAsync(Window window)
    {
        try
        {
            Logger.Log("Opening file picker for Check Hash...");
            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = L["Dialog_SelectCheckFiles"]
            });

            var paths = result.Select(x => x.Path.LocalPath);
            await AddFilesAsync(paths);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error adding files: {ex.Message}", LogLevel.Error);
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], ex.Message);
        }
    }

    public async Task AddFilesAsync(IEnumerable<string> filePaths)
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
                        Files.AddRange(newItems);
                        return Task.CompletedTask;
                    });
                }

                if (skippedFiles.Count > 0)
                {
                    var fileList = string.Join("\n", skippedFiles.Take(10));
                    if (skippedFiles.Count > 10) fileList += "\n...";

                    var summaryMsg = string.Format(L["Msg_FileSizeLimitExceeded_Summary"], skippedFiles.Count,
                        config.FileSizeLimitValue, config.FileSizeLimitUnit, fileList);

                    await RunOnUIAsync(async () => await MessageBoxHelper.ShowAsync(L["Msg_Error"], summaryMsg));
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

    private async Task RunOnUIAsync(Func<Task> action)
    {
        if (Application.Current == null)
        {
            await action();
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
        catch
        {
            await action();
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


    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void RemoveFile(FileItem item)
    {
        item.Cts?.Cancel();
        if (Files.Contains(item))
        {
            Files.Remove(item);
            item.IsDeleted = true;
            OnPropertyChanged(nameof(TotalFilesText));
            VerifyAllCommand.NotifyCanExecuteChanged();
            Logger.Log($"Removed check item: {item.FileName}");
        }
    }

    [RelayCommand]
    private async Task VerifySingleAsync(FileItem item)
    {
        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }

        await VerifyItemLogicAsync(item);
    }

private async Task VerifyItemLogicAsync(FileItem file)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            file.IsProcessing = true;
            file.IsMatch = null;
            file.ProcessDuration = "";
            file.Status = L["Status_Waiting"];
        });

        file.Cts = new CancellationTokenSource();
        if (Prefs.IsFileTimeoutEnabled) file.Cts.CancelAfter(TimeSpan.FromSeconds(Prefs.FileTimeoutSeconds));

        await Task.Run(async () =>
        {
            var result = await VerifyFileInternalAsync(file, file.Cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.NewExpectedHash != null) file.ExpectedHash = result.NewExpectedHash;

                file.Status = result.Status;
                file.IsMatch = result.IsMatch;
                file.ProcessDuration = result.Duration;
                file.IsProcessing = false;
                file.Cts?.Dispose();
                file.Cts = null;
            });
        });
    }

    private record struct VerificationResult(
        string Status,
        bool? IsMatch,
        string Duration,
        string? NewExpectedHash = null
    );

    private async Task<VerificationResult> VerifyFileInternalAsync(FileItem file, CancellationToken ct)
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
                var actualHash = await _hashService.ComputeHashAsync(file.FilePath, algoToCheck, ct);

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

    [RelayCommand]
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
                    string.Format(L["Msg_WrongHashFileContent"], item.FileName, hashFileName));
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
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_ReadHashError"]);
                Logger.Log($"Error reading hash file: {ex.Message}", LogLevel.Error);
            }
        }
    }
}