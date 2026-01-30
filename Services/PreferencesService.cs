using System;
using CheckHash.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class PreferencesService : ObservableObject
{
    [ObservableProperty] private FileSizeUnit _fileSizeLimitUnit = FileSizeUnit.GB;
    [ObservableProperty] private double _fileSizeLimitValue = 10;
    [ObservableProperty] private int _fileTimeoutSeconds = 60;

    // Limit file size
    [ObservableProperty] private bool _isFileSizeLimitEnabled;

    // File Timeout
    [ObservableProperty] private bool _isFileTimeoutEnabled;

    [ObservableProperty] private bool _isHashMaskingEnabled;
    public static PreferencesService Instance { get; } = new();

    // Force Cancel Event
    public event EventHandler? ForceCancelRequested;

    public void RequestForceCancel()
    {
        ForceCancelRequested?.Invoke(this, EventArgs.Empty);
    }

    // Calculate max size in bytes
    public long GetMaxSizeBytes()
    {
        if (!IsFileSizeLimitEnabled) return long.MaxValue;

        double multiplier = FileSizeLimitUnit switch
        {
            FileSizeUnit.Byte => 1,
            FileSizeUnit.KB => 1024,
            FileSizeUnit.MB => 1024 * 1024,
            FileSizeUnit.GB => 1024 * 1024 * 1024,
            FileSizeUnit.TB => 1024L * 1024 * 1024 * 1024,
            FileSizeUnit.PB => 1024L * 1024 * 1024 * 1024 * 1024,
            _ => 1
        };

        try
        {
            return (long)(FileSizeLimitValue * multiplier);
        }
        catch
        {
            return long.MaxValue;
        }
    }
}