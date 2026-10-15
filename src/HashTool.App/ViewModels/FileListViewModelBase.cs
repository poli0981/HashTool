using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Infrastructure;
using HashTool.App.Models;
using HashTool.App.Services;
using HashTool.Core.Files;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;
using Microsoft.Extensions.Logging;

namespace HashTool.App.ViewModels;

/// <summary>Services shared by the Create and Verify pages.</summary>
public sealed record FileListServices(
    LocalizationService Localization,
    SettingsService Settings,
    IDialogService Dialogs,
    IStorageService Storage,
    IClipboardService Clipboard,
    HashEngine Engine,
    ILoggerFactory LoggerFactory);

/// <summary>What happened while adding files, summarised for the user afterwards.</summary>
public sealed class ImportReport
{
    public int Added;

    /// <summary>Existing rows that received an expected hash (Verify).</summary>
    public int Merged;

    public int Duplicates;
    public readonly List<string> TooLarge = [];
    public readonly List<string> Errors = [];
    public int OverLimit;
}

/// <summary>
/// File list, filtering, importing and batch hashing shared by the Create and Verify pages. All list mutations happen on
/// the UI thread; enumeration and hashing run in the background.
/// </summary>
public abstract partial class FileListViewModelBase : ObservableObject, IHashJobObserver<FileItem>
{
    private const int UiBatchSize = 1000;

    private readonly List<FileItem> _all = [];
    private readonly Dictionary<string, FileItem> _byPath = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(FileItem Item, HashJobResult? Result)> _events = new();
    private readonly List<FileItem> _active = [];
    private readonly DispatcherTimer _filterTimer;
    private DispatcherTimer? _pump;
    private CancellationTokenSource? _batchCts;
    private Stopwatch _batchClock = new();
    private long _batchBytesDone;
    private long _batchBytesTotal;

    [ObservableProperty] private LocalizationProxy _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModify))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand), nameof(AddFolderCommand), nameof(ClearListCommand),
        nameof(ClearResultsCommand), nameof(ClearFailedCommand), nameof(RunAllCommand), nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModify))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand), nameof(AddFolderCommand), nameof(ClearListCommand),
        nameof(ClearResultsCommand), nameof(ClearFailedCommand), nameof(RunAllCommand))]
    private bool _isImporting;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private FileStatus? _selectedStatusFilter;
    [ObservableProperty] private FileSizeFilter _selectedSizeFilter = FileSizeFilter.All;
    [ObservableProperty] private FileItem? _selectedItem;

    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private int _queuedCount;
    [ObservableProperty] private int _succeededCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _cancelledCount;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _remainingText = string.Empty;
    [ObservableProperty] private string _speedText = string.Empty;

    protected FileListViewModelBase(FileListServices services)
    {
        Services = services;
        Logger = services.LoggerFactory.CreateLogger(GetType());
        _localization = new LocalizationProxy(services.Localization);
        services.Localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(services.Localization);
            OnPropertyChanged(nameof(TotalFilesText));
            UpdateBatchTexts();
        };
        _filterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => ApplyFilterNow());
    }

    protected FileListServices Services { get; }

    protected ILogger Logger { get; }

    protected LocalizationService L => Services.Localization;

    protected Core.Configuration.AppSettings Settings => Services.Settings.Current;

    /// <summary>Visible (filtered) items.</summary>
    public AvaloniaList<FileItem> Items { get; } = [];

    /// <summary>All items, including those hidden by the filter.</summary>
    protected IReadOnlyList<FileItem> AllItems => _all;

    public IReadOnlyList<HashType> Algorithms { get; } = HashAlgorithms.All;

    public IReadOnlyList<FileStatus?> StatusFilterOptions { get; } =
        [null, FileStatus.Ready, FileStatus.Processing, FileStatus.Success, FileStatus.Failure, FileStatus.Cancelled];

    public IReadOnlyList<FileSizeFilter> SizeFilterOptions { get; } = Enum.GetValues<FileSizeFilter>();

    public bool HasItems => _all.Count > 0;

    public bool CanModify => !IsBusy && !IsImporting;

    public string TotalFilesText => Items.Count == _all.Count
        ? L.Format("Lbl_TotalFiles", _all.Count)
        : $"{L.Format("Lbl_TotalFiles", Items.Count)} / {_all.Count}";

    /// <summary>Raised when the view should move keyboard focus to the search box.</summary>
    public event EventHandler? FocusSearchRequested;

    public void RequestSearchFocus() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------ filtering

    partial void OnSearchTextChanged(string value) => RestartFilterTimer();

    partial void OnSelectedStatusFilterChanged(FileStatus? value) => ApplyFilterNow();

    partial void OnSelectedSizeFilterChanged(FileSizeFilter value) => ApplyFilterNow();

    private void RestartFilterTimer()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    protected void ApplyFilterNow()
    {
        _filterTimer.Stop();
        var visible = _all.Where(Matches).ToList();
        Items.Clear();
        Items.AddRange(visible);
        OnPropertyChanged(nameof(TotalFilesText));
    }

    private bool Matches(FileItem item)
    {
        if (!string.IsNullOrWhiteSpace(SearchText) && !item.FileName.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
            return false;
        if (SelectedStatusFilter is { } state && item.State != state) return false;
        return SelectedSizeFilter switch
        {
            FileSizeFilter.Small => item.Size < ByteSize.OneMB,
            FileSizeFilter.Medium => item.Size is >= ByteSize.OneMB and < 100L * ByteSize.OneMB,
            FileSizeFilter.Large => item.Size is >= 100L * ByteSize.OneMB and < ByteSize.OneGB,
            FileSizeFilter.ExtraLarge => item.Size >= ByteSize.OneGB,
            _ => true
        };
    }

    // ------------------------------------------------------------------ adding files

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task AddFilesAsync()
    {
        var files = await Services.Storage.PickFilesAsync(L[PickFilesTitleKey]);
        if (files.Count > 0) await AddPathsAsync(files);
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task AddFolderAsync()
    {
        var folders = await Services.Storage.PickFoldersAsync(L["Dialog_SelectFolder"]);
        if (folders.Count == 0) return;
        if (Settings.IsMaxFolderCountEnabled && folders.Count > Settings.MaxFolderCount)
        {
            await Services.Dialogs.ShowAsync(L["Msg_Error"], L.Format("Msg_FolderLimit", Settings.MaxFolderCount), DialogIcon.Warning);
            return;
        }

        await AddPathsAsync(folders, fromFolderPicker: true);
    }

    protected virtual string PickFilesTitleKey => "Dialog_SelectFiles";

    /// <summary>Adds files and folders (dropped, picked, or passed on the command line).</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths, bool fromFolderPicker = false)
    {
        if (paths.Count == 0) return;
        if (!CanModify)
        {
            await Services.Dialogs.ShowAsync(L["Msg_Error"], L["Msg_BusyTryLater"], DialogIcon.Warning);
            return;
        }

        IsImporting = true;
        var report = new ImportReport();
        try
        {
            var hasFolders = fromFolderPicker || paths.Any(Directory.Exists);
            var maxFiles = hasFolders && Settings.IsMaxFileCountEnabled ? Settings.MaxFileCount : int.MaxValue;
            var maxSize = Settings.MaxFileSizeBytes();
            var options = new FileEnumerationOptions { IncludeHidden = Settings.IncludeHiddenFiles };
            var known = new HashSet<string>(_byPath.Keys, _byPath.Comparer);

            await Task.Run(async () =>
            {
                var batch = new List<FileEntry>(UiBatchSize);
                var accepted = 0;
                foreach (var entry in FileEnumerator.Expand(paths, options, (path, ex) =>
                         {
                             lock (report) report.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                         }))
                {
                    if (!known.Add(entry.FullPath))
                    {
                        report.Duplicates++;
                        continue;
                    }

                    if (entry.Length > maxSize)
                    {
                        report.TooLarge.Add(Path.GetFileName(entry.FullPath));
                        continue;
                    }

                    if (accepted >= maxFiles)
                    {
                        report.OverLimit++;
                        continue;
                    }

                    accepted++;
                    batch.Add(entry);
                    if (batch.Count >= UiBatchSize)
                    {
                        await AddBatchAsync(batch, report).ConfigureAwait(false);
                        batch = new List<FileEntry>(UiBatchSize);
                    }
                }

                if (batch.Count > 0) await AddBatchAsync(batch, report).ConfigureAwait(false);
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Adding files failed");
            report.Errors.Add(ex.Message);
        }
        finally
        {
            IsImporting = false;
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(TotalFilesText));
        }

        Logger.LogInformation("Added {Added} files ({Duplicates} duplicates, {TooLarge} too large, {OverLimit} over the limit, {Errors} errors)",
            report.Added, report.Duplicates, report.TooLarge.Count, report.OverLimit, report.Errors.Count);
        await ShowImportReportAsync(report);
    }

    private async Task AddBatchAsync(List<FileEntry> entries, ImportReport report)
    {
        var items = await CreateItemsAsync(entries, report).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => AddItems(items, report));
    }

    /// <summary>Turns files into list items (runs on a background thread). Verify reads checksum files here.</summary>
    protected abstract Task<IReadOnlyList<FileItem>> CreateItemsAsync(IReadOnlyList<FileEntry> files, ImportReport report);

    /// <summary>Adds items, or merges them into existing rows for the same path (UI thread).</summary>
    protected void AddItems(IEnumerable<FileItem> items, ImportReport report)
    {
        var added = new List<FileItem>();
        foreach (var item in items)
        {
            if (_byPath.TryGetValue(item.FilePath, out var existing))
            {
                if (Merge(existing, item)) report.Merged++;
                else report.Duplicates++;
                continue;
            }

            _byPath[item.FilePath] = item;
            _all.Add(item);
            added.Add(item);
        }

        report.Added += added.Count;
        var visible = added.Where(Matches).ToList();
        if (visible.Count > 0) Items.AddRange(visible);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(TotalFilesText));
    }

    /// <summary>Lets Verify copy an expected hash onto a row that already exists. Returns true when merged.</summary>
    protected virtual bool Merge(FileItem existing, FileItem incoming) => false;

    protected FileItem? FindItem(string path) => _byPath.GetValueOrDefault(path);

    private async Task ShowImportReportAsync(ImportReport report)
    {
        var lines = new List<string>();
        if (report.OverLimit > 0) lines.Add(L.Format("Msg_FileLimit", report.Added, report.OverLimit));
        if (report.TooLarge.Count > 0)
        {
            var names = string.Join("\n", report.TooLarge.Take(10)) + (report.TooLarge.Count > 10 ? "\n…" : "");
            lines.Add(L.Format("Msg_FileSizeLimitExceeded_Summary", report.TooLarge.Count, Settings.FileSizeLimitValue,
                Settings.FileSizeLimitUnit, names));
        }

        if (report.Errors.Count > 0)
            lines.Add(L.Format("Msg_ImportErrors", report.Errors.Count, string.Join("\n", report.Errors.Take(10))));

        if (report.Added == 0 && report.Merged == 0 && report.Duplicates == 0 && lines.Count == 0) lines.Add(L["Msg_EmptyFolder"]);
        if (lines.Count > 0)
            await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], string.Join("\n\n", lines),
                report.Errors.Count > 0 ? DialogIcon.Warning : DialogIcon.Information);
    }

    // ------------------------------------------------------------------ list commands

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ClearListAsync()
    {
        if (_all.Count == 0) return;
        if (_all.Count > 20 &&
            !await Services.Dialogs.ConfirmAsync(L["Btn_Clear"], L.Format("Msg_ConfirmClear", _all.Count), DialogIcon.Warning))
            return;

        foreach (var item in _all) item.IsRemoved = true;
        _all.Clear();
        _byPath.Clear();
        Items.Clear();
        ResetBatchStats();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(TotalFilesText));
        Logger.LogInformation("List cleared");
    }

    [RelayCommand]
    private void RemoveItem(FileItem? item)
    {
        if (item is null || item.IsProcessing) return;
        item.Cancellation?.Cancel();
        item.IsRemoved = true;
        _all.Remove(item);
        _byPath.Remove(item.FilePath);
        Items.Remove(item);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(TotalFilesText));
    }

    /// <summary>Delete key: removes the selected row.</summary>
    [RelayCommand]
    private void RemoveSelected()
    {
        if (CanModify && SelectedItem is { } item) RemoveItem(item);
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private void ClearResults()
    {
        foreach (var item in Items) ResetItem(item);
        ResetBatchStats();
        ApplyFilterNow();
    }

    [RelayCommand]
    private void ClearItemResult(FileItem? item)
    {
        if (item is null || item.IsProcessing || !CanModify) return;
        ResetItem(item);
    }

    protected virtual void ResetItem(FileItem item) => item.ResetResult();

    [RelayCommand(CanExecute = nameof(CanModify))]
    private void ClearFailed()
    {
        var failed = _all.Where(IsFailedForCleanup).ToList();
        foreach (var item in failed)
        {
            item.IsRemoved = true;
            _all.Remove(item);
            _byPath.Remove(item.FilePath);
            Items.Remove(item);
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(TotalFilesText));
        Logger.LogInformation("Removed {Count} failed or cancelled items", failed.Count);
    }

    protected virtual bool IsFailedForCleanup(FileItem item) => item.State is FileStatus.Failure or FileStatus.Cancelled;

    [RelayCommand(CanExecute = nameof(CanRunAll))]
    private Task RunAllAsync() => RunBatchAsync(Items.Where(i => !i.IsProcessing).ToList());

    private bool CanRunAll() => CanModify && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        Logger.LogInformation("Cancel requested");
        _batchCts?.Cancel();
    }

    /// <summary>Row button: starts this file, or stops it while it is being processed.</summary>
    [RelayCommand]
    private async Task RunItemAsync(FileItem? item)
    {
        if (item is null) return;
        if (item.IsProcessing)
        {
            item.Cancellation?.Cancel();
            return;
        }

        if (CanModify) await RunBatchAsync([item]);
    }

    [RelayCommand]
    private async Task CopyTextAsync(string? text)
    {
        if (!string.IsNullOrEmpty(text)) await Services.Clipboard.SetTextAsync(text);
    }

    // ------------------------------------------------------------------ batch processing

    /// <summary>Prepares items before hashing (e.g. Verify loads sidecar files); returns the items to hash.</summary>
    protected virtual Task<IReadOnlyList<FileItem>> PrepareAsync(IReadOnlyList<FileItem> items) => Task.FromResult(items);

    protected abstract HashJob? CreateJob(FileItem item);

    /// <summary>Applies a finished job to its row (UI thread).</summary>
    protected abstract void ApplyResult(FileItem item, HashJobResult result);

    protected virtual ItemStatus QueuedStatus => ItemStatus.Queued;

    protected async Task RunBatchAsync(IReadOnlyList<FileItem> items)
    {
        if (items.Count == 0 || !CanModify) return;
        IsBusy = true;
        ResetBatchStats();
        _batchCts = new CancellationTokenSource();
        try
        {
            var toHash = await PrepareAsync(items);
            foreach (var item in toHash)
            {
                item.ResetResult(QueuedStatus);
                item.Cancellation?.Dispose();
                item.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_batchCts.Token);
            }

            QueuedCount = toHash.Count;
            _batchBytesTotal = toHash.Sum(i => i.Size);
            _batchClock = Stopwatch.StartNew();
            StartPump();
            Logger.LogInformation("Processing {Count} files ({Bytes})", toHash.Count, ByteSize.Format(_batchBytesTotal));

            var processor = new BatchProcessor(Services.Engine, new BatchOptions
            {
                MaxParallelFiles = Settings.MaxParallelFiles,
                SequentialDisk = Settings.OptimizeForHdd,
                PerFileTimeout = Settings.IsFileTimeoutEnabled ? TimeSpan.FromSeconds(Settings.FileTimeoutSeconds) : null
            });
            await Task.Run(() => processor.RunAsync(toHash, CreateJob, this, _batchCts.Token));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Batch failed");
            await Services.Dialogs.ShowErrorAsync(L["Msg_Error"], L.Format("Msg_UnexpectedError", ex.Message), ex.ToString());
        }
        finally
        {
            StopPump();
            // Every item has reported by now; apply all of it (one pump tick only handles a slice of a large batch).
            while (!_events.IsEmpty) DrainEvents();
            _batchClock.Stop();
            UpdateBatchTexts();
            IsBusy = false;
            _batchCts.Dispose();
            _batchCts = null;
            ApplyFilterNow();
        }

        Logger.LogInformation("Finished in {Elapsed}: {Succeeded} succeeded, {Failed} failed, {Cancelled} cancelled",
            _batchClock.Elapsed, SucceededCount, FailedCount, CancelledCount);
        await OnBatchFinishedAsync(items);
    }

    /// <summary>Shows the result summary; Verify also offers the elevated restart on access errors.</summary>
    protected virtual async Task OnBatchFinishedAsync(IReadOnlyList<FileItem> items)
    {
        if (items.Count <= 1) return;
        var icon = FailedCount > 0 || CancelledCount > 0 ? DialogIcon.Warning : DialogIcon.Success;
        var message = $"{L.Format("Msg_Result_Content", SucceededCount, FailedCount, CancelledCount)}\n{L.Format("Msg_TaskDuration", FormatClock(BatchElapsed))}";
        await Services.Dialogs.ShowAsync(L["Msg_Result_Title"], message, icon);
        await OfferElevationAsync(items);
    }

    protected async Task OfferElevationAsync(IReadOnlyList<FileItem> items)
    {
        if (!Elevation.CanRestartElevated || !items.Any(i => i.Status == ItemStatus.AccessDenied)) return;
        if (!await Services.Dialogs.ConfirmAsync(L["Btn_RestartAdmin"], L["Msg_AccessDeniedRestart"], DialogIcon.Question)) return;
        if (Elevation.RestartElevated(ElevationVerb, _all.Select(i => SourcePathForRestart(i)).Distinct(_byPath.Comparer).ToList()))
            RequestShutdown?.Invoke(this, EventArgs.Empty);
    }

    protected abstract ActivationVerb ElevationVerb { get; }

    protected virtual string SourcePathForRestart(FileItem item) => item.SourceChecksumFile ?? item.FilePath;

    /// <summary>Raised when the app should close (after handing over to an elevated instance).</summary>
    public event EventHandler? RequestShutdown;

    void IHashJobObserver<FileItem>.OnStarted(FileItem item) => _events.Enqueue((item, null));

    void IHashJobObserver<FileItem>.OnProgress(FileItem item, long bytesRead)
    {
        item.AddBytesRead(bytesRead);
        Interlocked.Add(ref _batchBytesDone, bytesRead);
    }

    void IHashJobObserver<FileItem>.OnCompleted(FileItem item, HashJobResult result) => _events.Enqueue((item, result));

    private void StartPump()
    {
        _pump ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            DrainEvents();
            UpdateBatchTexts();
        });
        _pump.Start();
    }

    private void StopPump() => _pump?.Stop();

    private void DrainEvents()
    {
        var budget = 2000;
        while (budget-- > 0 && _events.TryDequeue(out var e))
        {
            if (e.Result is null)
            {
                e.Item.IsProcessing = true;
                e.Item.State = FileStatus.Processing;
                e.Item.SetStatus(ItemStatus.Hashing, e.Item.Algorithm.ToString());
                e.Item.ShowProgress = e.Item.Size >= FileItem.ProgressThreshold;
                _active.Add(e.Item);
                continue;
            }

            _active.Remove(e.Item);
            e.Item.IsProcessing = false;
            e.Item.ShowProgress = false;
            e.Item.Duration = FileItem.FormatDuration(e.Result.Elapsed);
            ApplyResult(e.Item, e.Result);
            ProcessedCount++;
            switch (e.Item.State)
            {
                case FileStatus.Success:
                    SucceededCount++;
                    break;
                case FileStatus.Cancelled:
                    CancelledCount++;
                    break;
                default:
                    FailedCount++;
                    break;
            }
        }

        foreach (var item in _active)
        {
            if (item.ShowProgress && item.Size > 0) item.Progress = Math.Min(1, (double)item.BytesRead / item.Size);
        }
    }

    private void ResetBatchStats()
    {
        ProcessedCount = QueuedCount = SucceededCount = FailedCount = CancelledCount = 0;
        _batchBytesDone = 0;
        _batchBytesTotal = 0;
        ProgressValue = 0;
        ProgressText = DetailText = RemainingText = SpeedText = string.Empty;
    }

    private void UpdateBatchTexts()
    {
        var total = QueuedCount;
        if (total == 0) return;
        var done = Interlocked.Read(ref _batchBytesDone);
        ProgressValue = _batchBytesTotal > 0 ? Math.Min(1, (double)done / _batchBytesTotal) : (double)ProcessedCount / total;
        ProgressText = L.Format("Lbl_ProgressStats", ProcessedCount, total, ProgressValue * 100);
        DetailText = L.Format("Lbl_DetailedStats", SucceededCount, Percent(SucceededCount), FailedCount, Percent(FailedCount),
            CancelledCount, Percent(CancelledCount));

        var elapsed = _batchClock.Elapsed.TotalSeconds;
        var rate = elapsed > 0.5 ? done / elapsed : 0;
        SpeedText = Settings.ShowReadWriteSpeed && rate > 0 ? L.Format("Lbl_ReadSpeed", ByteSize.Format((long)rate)) : string.Empty;
        RemainingText = IsBusy && rate > 0 && done < _batchBytesTotal
            ? L.Format("Msg_EstimatedTime", FormatClock(TimeSpan.FromSeconds((_batchBytesTotal - done) / rate)))
            : IsBusy ? L["Msg_TimeUnknown"] : string.Empty;

        double Percent(int count) => total > 0 ? count * 100.0 / total : 0;
    }

    protected static string FormatClock(TimeSpan time) => $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";

    protected TimeSpan BatchElapsed => _batchClock.Elapsed;

    /// <summary>Maps a failed job to the row's status.</summary>
    protected static void ApplyFailure(FileItem item, HashJobResult result)
    {
        item.IsMatch = result.Status == HashJobStatus.Succeeded ? item.IsMatch : false;
        switch (result.Status)
        {
            case HashJobStatus.Cancelled:
                item.State = FileStatus.Cancelled;
                item.IsMatch = null;
                item.SetStatus(ItemStatus.Cancelled);
                return;
            case HashJobStatus.TimedOut:
                item.State = FileStatus.Cancelled;
                item.IsMatch = null;
                item.SetStatus(ItemStatus.TimedOut);
                return;
        }

        item.State = FileStatus.Failure;
        item.SetStatus(FileErrors.Classify(result.Error) switch
        {
            FileErrorKind.NotFound => ItemStatus.NotFound,
            FileErrorKind.AccessDenied => ItemStatus.AccessDenied,
            FileErrorKind.InUse => ItemStatus.InUse,
            _ => ItemStatus.Failed
        }, result.Error?.Message);
    }
}
