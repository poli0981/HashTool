using System;
using System.Collections.Generic;
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

namespace CheckHash.ViewModels;

public partial class CreateHashViewModel : ObservableObject, IDisposable
{
    private readonly HashService _hashService = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComputeAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearListCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompressFilesCommand))]
    private bool _isComputing;

    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);

    public CreateHashViewModel()
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

    public string TotalFilesText => string.Format(L["Lbl_TotalFiles"], Files.Count);

    public AvaloniaList<FileItem> Files { get; } = new();

    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());

    private bool CanComputeAll => Files.Count > 0 && !IsComputing;
    private bool CanModifyList => !IsComputing;
    private bool CanCompress => Files.Count > 0 && !IsComputing;

    public void Dispose()
    {
        Prefs.ForceCancelRequested -= OnForceCancelRequested;
        foreach (var file in Files) file.Cts?.Dispose();
    }

    private void OnForceCancelRequested(object? sender, EventArgs e)
    {
        Logger.Log("Force Cancel requested by user.", LogLevel.Warning);
        foreach (var file in Files) file.Cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private async Task AddFiles(Window window)
    {
        Logger.Log("Opening file picker for Create Hash...");
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = L["Dialog_SelectFiles"]
        });

        if (files.Count == 0) return;

        var config = ConfigService.Load();
        long limitBytes = 0;
        if (config.IsFileSizeLimitEnabled) limitBytes = Prefs.GetMaxSizeBytes();

        // Snapshot existing paths to avoid concurrent access issues
        var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath));

        IsComputing = true;
        try
        {
            await Task.Run(async () =>
            {
                var newItems = new List<FileItem>();

                foreach (var file in files)
                {
                    var path = file.Path.LocalPath;

                    // Check against snapshot + new items
                    if (existingPaths.Contains(path)) continue;
                    existingPaths.Add(path);

                    var info = new FileInfo(path);
                    var len = info.Length; // I/O

                    if (config.IsFileSizeLimitEnabled && len > limitBytes)
                    {
                        var msg = string.Format(L["Msg_FileSizeLimitExceeded"], file.Name, config.FileSizeLimitValue,
                            config.FileSizeLimitUnit);
                        Logger.Log(msg, LogLevel.Warning);
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                            await MessageBoxHelper.ShowAsync(L["Msg_Error"], msg));
                        continue;
                    }

                    var item = new FileItem
                    {
                        FileName = file.Name,
                        FilePath = path,
                        FileSize = FileItem.FormatSize(len),
                        SelectedAlgorithm = HashType.SHA256
                    };
                    newItems.Add(item);
                    Logger.Log($"Added file: {file.Name}");
                }

                if (newItems.Count > 0)
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Files.AddRange(newItems);
                    });
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


                await Task.Run(() =>
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);

                    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    foreach (var item in filesToCompress)
                        if (!string.IsNullOrEmpty(item.ResultHash))
                        {
                            var ext = item.SelectedAlgorithm.ToString().ToLower();
                            var hashFileName = $"{item.FileName}.{ext}";

                            var entry = archive.CreateEntry(hashFileName);
                            using var entryStream = entry.Open();
                            using var writer = new StreamWriter(entryStream);
                            writer.Write(item.ResultHash);
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

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void ClearList()
    {
        foreach (var file in Files)
        {
            file.Cts?.Cancel();
            file.Cts?.Dispose();
            file.IsDeleted = true;
        }

        Files.Clear();
        OnPropertyChanged(nameof(TotalFilesText));
        ComputeAllCommand.NotifyCanExecuteChanged();
        Logger.Log("Cleared file list.");
    }

    [RelayCommand(CanExecute = nameof(CanComputeAll))]
    private async Task ComputeAll()
    {
        Logger.Log("Starting batch computation...");
        IsComputing = true;
        var success = 0;
        var fail = 0;
        var cancelled = 0;
        var queue = Files.ToList();

        await Parallel.ForEachAsync(queue, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
        }, async (file, ct) =>
        {
            if (file.IsDeleted) return;

            await ProcessItemAsync(file);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (file.Status == L["Status_Done"]) success++;
                else if (file.Status == L["Status_Cancelled"]) cancelled++;
                else fail++;
            });
        });

        IsComputing = false;
        Logger.Log($"Batch finished. Success: {success}, Failed: {fail}, Cancelled: {cancelled}");

        if (cancelled > 0)
        {
            var msg = L["Msg_TaskCancelled_Content"];
            Logger.Log(msg, LogLevel.Warning);
            await MessageBoxHelper.ShowAsync(L["Msg_TaskCancelled_Title"], msg);
        }
        else
        {
            await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"],
                string.Format(L["Msg_Result_Content"], success, fail));
        }
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

        try
        {
            var hash = await _hashService.ComputeHashAsync(item.FilePath, item.SelectedAlgorithm, item.Cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.ResultHash = hash;
                item.Status = L["Status_Done"];
            });
            Logger.Log($"Computed {item.FileName}: {hash}", LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => item.Status = L["Status_Cancelled"]);
            Logger.Log($"Cancelled computation for {item.FileName}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => item.Status = string.Format(L["Status_Error"], ex.Message));
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
        if (item.IsProcessing)
        {
            item.Cts?.Cancel();
            return;
        }

        await ProcessItemAsync(item);
    }

    [RelayCommand]
    private void RemoveFile(FileItem item)
    {
        item.Cts?.Cancel();
        item.Cts?.Dispose();
        item.IsDeleted = true;
        if (Files.Contains(item))
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(TotalFilesText));
            ComputeAllCommand.NotifyCanExecuteChanged();
            Logger.Log($"Removed file: {item.FileName}");
        }
    }
}