using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckHash.Services;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CheckHash.ViewModels;

public partial class CheckHashViewModel : ObservableObject, IDisposable
{
    public LocalizationService Localization => LocalizationService.Instance;
    private LocalizationService L => LocalizationService.Instance;
    private readonly HashService _hashService = new();
    private ConfigurationService ConfigService => ConfigurationService.Instance;
    private PreferencesService Prefs => PreferencesService.Instance;
    private LoggerService Logger => LoggerService.Instance;

    // List file to check
    public ObservableCollection<FileItem> Files { get; } = new();

    // Hash Algorithms
    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());
    [ObservableProperty] private HashType _globalAlgorithm = HashType.SHA256;

    // Progress Bar
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 100;
    [ObservableProperty] private bool _isChecking;

    public string TotalFilesText => string.Format(L["Lbl_TotalFiles"], Files.Count);

    private const long MaxHashFileSize = 1024 * 1024; // 1MB

    public CheckHashViewModel()
    {
        // Force cancel event
        Prefs.ForceCancelRequested += OnForceCancelRequested;
    }

    public void Dispose()
    {
        Prefs.ForceCancelRequested -= OnForceCancelRequested;
        foreach (var file in Files)
        {
            file.Cts?.Dispose();
        }
    }

    private void OnForceCancelRequested(object? sender, EventArgs e)
    {
        Logger.Log("Force Cancel requested by user (Check Hash).", LogLevel.Warning);
        foreach (var file in Files)
        {
            file.Cts?.Cancel();
        }
    }

    [RelayCommand]
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

    private bool CanVerify => Files.Count > 0 && !IsChecking;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAll()
    {
        Logger.Log("Starting batch verification...");
        IsChecking = true;
        ProgressMax = Files.Count;
        ProgressValue = 0;
        var queue = Files.ToList();
        int cancelled = 0;
        
        await Parallel.ForEachAsync(queue, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, async (file, ct) =>
        {
            // Thread-safe check if file still exists in the collection
            bool exists = await Dispatcher.UIThread.InvokeAsync(() => Files.Contains(file));
            if (!exists) return;
            
            await VerifyItemLogic(file);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue++;
                if (file.Status == L["Status_Cancelled"]) cancelled++;
            });
        });
        
        IsChecking = false;
        
        // Count results to show summary
        int match = Files.Count(f => f.IsMatch == true);
        int failCount = Files.Count(f => f.IsMatch == false);
        
        Logger.Log($"Batch verification finished. Match: {match}, Mismatch/Error: {failCount}, Cancelled: {cancelled}", LogLevel.Info);
        
        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"], 
            string.Format(L["Msg_CheckResult"], Files.Count, match, failCount, cancelled));
    }
    
    [RelayCommand]
    private async Task AddFilesToCheck(Avalonia.Controls.Window window)
    {
        try
        {
            Logger.Log("Opening file picker for Check Hash...");
            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions 
            { 
                AllowMultiple = true,
                Title = L["Dialog_SelectCheckFiles"]
            });

            var config = ConfigService.Load();
            long limitBytes = 0;
            if (config.IsFileSizeLimitEnabled)
            {
                limitBytes = Prefs.GetMaxSizeBytes();
            }

            foreach (var file in result)
            {
                var path = file.Path.LocalPath;
                var info = new FileInfo(path);

                // 5. Check file size limit
                if (config.IsFileSizeLimitEnabled && info.Length > limitBytes)
                {
                    var msg = $"File {file.Name} exceeds the size limit of {config.FileSizeLimitValue} {config.FileSizeLimitUnit}.";
                    Logger.Log(msg, LogLevel.Warning);
                    await MessageBoxHelper.ShowAsync(L["Msg_Error"], msg);
                    continue;
                }

                // TrimStart('.') is to avoid leading dot in extension
                var ext = Path.GetExtension(path).TrimStart('.').ToUpper();
                
                // Parse extension to see if it's a known hash file
                var isHashFile = Enum.TryParse<HashType>(ext, true, out var detectedAlgo);
                
                if (isHashFile)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (dir == null) continue; // Continue if folder not found

                    var sourcePath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path));
                    
                    // Check file size limit of original file
                    if (File.Exists(sourcePath))
                    {
                        var sourceInfo = new FileInfo(sourcePath);
                        if (config.IsFileSizeLimitEnabled && sourceInfo.Length > limitBytes)
                        {
                            var msg = $"File {Path.GetFileName(sourcePath)} exceeds the size limit of {config.FileSizeLimitValue} {config.FileSizeLimitUnit}.";
                            Logger.Log(msg, LogLevel.Warning);
                            await MessageBoxHelper.ShowAsync(L["Msg_Error"], msg);
                            continue; // Bỏ qua file này
                        }
                    }

                    var item = new FileItem
                    {
                        FileName = Path.GetFileName(sourcePath),
                        FilePath = sourcePath,
                        ExpectedHash = await ReadHashFromFile(path),
                        Status = File.Exists(sourcePath) ? L["Status_ReadyFromHash"] : L["Status_MissingOriginal"],
                        SelectedAlgorithm = detectedAlgo
                    };
                    
                    if (File.Exists(sourcePath))
                    {
                        var sourceInfo = new FileInfo(sourcePath);
                        item.FileSize = FileItem.FormatSize(sourceInfo.Length);
                    }
                    else
                    {
                        item.IsMatch = false;
                    }
                    
                    Files.Add(item);
                    Logger.Log($"Added check item (from hash file): {item.FileName}");
                }
                else
                {
                    // Regular file to check
                    if (config.IsFileSizeLimitEnabled && info.Length > limitBytes)
                    {
                        var msg = $"File {file.Name} exceeds the size limit of {config.FileSizeLimitValue} {config.FileSizeLimitUnit}.";
                        Logger.Log(msg, LogLevel.Warning);
                        await MessageBoxHelper.ShowAsync(L["Msg_Error"], msg);
                        continue;
                    }

                    if (!Files.Any(f => f.FilePath == path))
                    {
                        Files.Add(new FileItem
                        {
                            FileName = file.Name,
                            FilePath = path,
                            FileSize = FileItem.FormatSize(info.Length),
                            Status = L["Status_Waiting"],
                            ExpectedHash = "" // Not set yet (user can input or load later)
                        });
                        Logger.Log($"Added check item: {file.Name}");
                    }
                }
            }
            
            OnPropertyChanged(nameof(TotalFilesText));
            VerifyAllCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error adding files: {ex.Message}", LogLevel.Error);
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], ex.Message);
        }
    }
    
    private async Task<string> ReadHashFromFile(string path)
    {
        try 
        {
            var info = new FileInfo(path);
            if (info.Length > MaxHashFileSize)
            {
                Logger.Log($"Hash file too large (>{MaxHashFileSize/1024}KB): {path}", LogLevel.Warning);
                return "";
            }

            var content = await File.ReadAllTextAsync(path);
            // Find the first hash-like string in the content
            var match = Regex.Match(content, @"[a-fA-F0-9]{32,128}");
            return match.Success ? match.Value : content.Trim();
        }
        catch { return ""; }
    }
    
    [RelayCommand]
    private void RemoveFile(FileItem item)
    {
        item.Cts?.Cancel();
        if (Files.Contains(item))
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(TotalFilesText));
            VerifyAllCommand.NotifyCanExecuteChanged();
            Logger.Log($"Removed check item: {item.FileName}");
        }
    }
    
    [RelayCommand]
    private async Task VerifySingle(FileItem item)
    {
        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }
        await VerifyItemLogic(item);
    }
    
    private async Task VerifyItemLogic(FileItem file)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            file.IsProcessing = true;
            file.IsMatch = null;
            file.ProcessDuration = "";
        });

        file.Cts = new CancellationTokenSource();
        
        // Set timeout if enabled
        if (Prefs.IsFileTimeoutEnabled)
        {
            file.Cts.CancelAfter(TimeSpan.FromSeconds(Prefs.FileTimeoutSeconds));
        }

        var sw = Stopwatch.StartNew();
        Logger.Log($"Verifying {file.FileName}...");

        try
        {
            // Step 1: Check file existence
            if (!File.Exists(file.FilePath))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    file.Status = L["Status_LostOriginal"];
                    file.IsMatch = false;
                });
                Logger.Log($"Original file missing: {file.FileName}", LogLevel.Error);
                return; // If file not found, skip further checks
            }
            
            if (string.IsNullOrWhiteSpace(file.ExpectedHash))
            {
                // If no expected hash, try to find sidecar file
                var globalExt = GlobalAlgorithm.ToString().ToLower();
                var sidecarPath = $"{file.FilePath}.{globalExt}";
                
                if (File.Exists(sidecarPath))
                {
                    var hash = await ReadHashFromFile(sidecarPath);
                    await Dispatcher.UIThread.InvokeAsync(() => file.ExpectedHash = hash);
                    Logger.Log($"Found sidecar hash for {file.FileName}");
                }
            }

            if (string.IsNullOrWhiteSpace(file.ExpectedHash))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    file.Status = L["Status_MissingHash"];
                    file.IsMatch = false;
                });
                Logger.Log($"Missing expected hash for {file.FileName}", LogLevel.Warning);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => file.Status = L["Status_Waiting"]);
                
                var algoToCheck = GlobalAlgorithm;

                var actualHash = await _hashService.ComputeHashAsync(file.FilePath, algoToCheck, file.Cts.Token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (string.Equals(actualHash, file.ExpectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        file.Status = L["Status_Valid"];
                        file.IsMatch = true;
                    }
                    else
                    {
                        file.Status = L["Status_Invalid"];
                        file.IsMatch = false;
                    }
                });

                if (file.IsMatch == true)
                    Logger.Log($"Verification VALID: {file.FileName}", LogLevel.Success);
                else
                    Logger.Log($"Verification INVALID: {file.FileName}. Expected: {file.ExpectedHash}, Actual: {actualHash}", LogLevel.Error);
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                file.Status = L["Status_Cancelled"];
                file.IsMatch = null;
            });
            Logger.Log($"Verification cancelled: {file.FileName}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => file.Status = ex.Message);
            Logger.Log($"Verification error {file.FileName}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            sw.Stop();
            var duration = sw.Elapsed.TotalSeconds < 1 ? $"{sw.ElapsedMilliseconds}ms" : $"{sw.Elapsed.TotalSeconds:F2}s";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                file.ProcessDuration = duration;
                file.IsProcessing = false;
            });

            file.Cts?.Dispose();
            file.Cts = null;
        }
    }
    
    [RelayCommand]
    private async Task BrowseHashFile(FileItem item)
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (window == null) return;

        // Open file picker to select hash file
        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = string.Format(L["Dialog_SelectHashFile"], item.FileName),
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var hashFilePath = result[0].Path.LocalPath;
            var hashFileName = Path.GetFileName(hashFilePath);
            
            // Check file name to see if it matches the original file
            // If not match, show warning
            // 1. Check file name
            if (!hashFileName.Contains(item.FileName, StringComparison.OrdinalIgnoreCase))
            {
                await MessageBoxHelper.ShowAsync(L["Msg_WrongHashFile"], 
                    string.Format(L["Msg_WrongHashFileContent"], item.FileName, hashFileName));
                return;
            }

            // 2. If all good, read hash from file
            try 
            {
                item.ExpectedHash = await ReadHashFromFile(hashFilePath);
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
