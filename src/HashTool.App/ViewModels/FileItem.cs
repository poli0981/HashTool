using System;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HashTool.App.Models;
using HashTool.Core.Hashing;

namespace HashTool.App.ViewModels;

/// <summary>What the status column shows. The text is looked up when displayed, so it follows language changes.</summary>
public enum ItemStatus
{
    Ready,
    Queued,
    Hashing,
    Done,
    Saved,
    Failed,
    NotFound,
    AccessDenied,
    InUse,
    TimedOut,
    Cancelled,
    Waiting,
    Valid,
    ValidLegacyCrc,
    Mismatch,
    LengthMismatch,
    MissingHash,
    FromChecksumFile,
    HashLoaded,
    NoHashInFile,
    UnreadableHashFile
}

/// <summary>One file in the Create or Verify list.</summary>
public sealed partial class FileItem : ObservableObject
{
    /// <summary>Files at least this large show a per-file progress bar while hashing.</summary>
    public const long ProgressThreshold = 64L * 1024 * 1024;

    private long _bytesRead;

    public FileItem(string path, long size, HashType algorithm)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        DirectoryName = Path.GetDirectoryName(path) ?? string.Empty;
        Size = size;
        _algorithm = algorithm;
    }

    public string FilePath { get; }

    public string FileName { get; }

    public string DirectoryName { get; }

    public long Size { get; }

    public string SizeText => Core.ByteSize.Format(Size);

    [ObservableProperty] private HashType _algorithm;

    /// <summary>The algorithm came from a checksum file (BSD tag, extension) and is not a guess.</summary>
    [ObservableProperty] private bool _isAlgorithmKnown;

    [ObservableProperty] private string _resultHash = string.Empty;

    /// <summary>The algorithm that produced <see cref="ResultHash"/>; exports use it, not the row's current choice.</summary>
    public HashType? ResultAlgorithm { get; private set; }
    [ObservableProperty] private string _expectedHash = string.Empty;
    [ObservableProperty] private FileStatus _state = FileStatus.Ready;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Ready;

    /// <summary>Extra status information, e.g. an error message or an algorithm name.</summary>
    [ObservableProperty] private string? _statusDetail;

    [ObservableProperty] private bool? _isMatch;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private string _duration = string.Empty;
    [ObservableProperty] private bool _isRevealed;
    [ObservableProperty] private bool _isProcessing;

    /// <summary>Checksum file the expected hash was read from, if any.</summary>
    public string? SourceChecksumFile { get; set; }

    public bool IsRemoved { get; set; }

    internal CancellationTokenSource? Cancellation { get; set; }

    internal long BytesRead => Interlocked.Read(ref _bytesRead);

    internal void AddBytesRead(long count) => Interlocked.Add(ref _bytesRead, count);

    internal void ResetProgress()
    {
        Interlocked.Exchange(ref _bytesRead, 0);
        Progress = 0;
        ShowProgress = false;
    }

    public void SetResult(HashType algorithm, string hash)
    {
        ResultAlgorithm = algorithm;
        ResultHash = hash;
    }

    /// <summary>A result computed with another algorithm no longer describes this row, so it is cleared.</summary>
    partial void OnAlgorithmChanged(HashType value)
    {
        if (ResultAlgorithm is { } produced && produced != value && !IsProcessing) ResetResult();
    }

    public void SetStatus(ItemStatus status, string? detail = null)
    {
        StatusDetail = detail;
        Status = status;
    }

    /// <summary>Clears computed results so the item can be processed again.</summary>
    public void ResetResult(ItemStatus status = ItemStatus.Ready)
    {
        ResultHash = string.Empty;
        ResultAlgorithm = null;
        IsMatch = null;
        Duration = string.Empty;
        State = FileStatus.Ready;
        ResetProgress();
        SetStatus(status);
    }

    internal static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalSeconds < 1 ? $"{elapsed.TotalMilliseconds:0} ms" : $"{elapsed.TotalSeconds:0.00} s";
}
