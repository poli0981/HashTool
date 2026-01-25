using System;
using CheckHash.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class PreferencesService : ObservableObject
{
    public static PreferencesService Instance { get; } = new();

    [ObservableProperty] private bool _isHashMaskingEnabled = false;
    
    // Limit file size
    [ObservableProperty] private bool _isFileSizeLimitEnabled = false;
    [ObservableProperty] private double _fileSizeLimitValue = 10;
    [ObservableProperty] private FileSizeUnit _fileSizeLimitUnit = FileSizeUnit.GB;

    // File Timeout
    [ObservableProperty] private bool _isFileTimeoutEnabled = false;
    [ObservableProperty] private int _fileTimeoutSeconds = 60;

    // Sự kiện Force Cancel
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