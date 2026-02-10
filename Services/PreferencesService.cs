using System;
using CheckHash.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class PreferencesService : ObservableObject
{
    [ObservableProperty] private FileSizeUnit _fileSizeLimitUnit = FileSizeUnit.GB;
    [ObservableProperty] private double _fileSizeLimitValue = 10;
    [ObservableProperty] private int _fileTimeoutSeconds = 60;
    [ObservableProperty] private bool _isFileSizeLimitEnabled;
    [ObservableProperty] private bool _isFileTimeoutEnabled;
    [ObservableProperty] private bool _isHashMaskingEnabled;
    public static PreferencesService Instance { get; } = new();
    public event EventHandler? ForceCancelRequested;

    public void RequestForceCancel()
    {
        ForceCancelRequested?.Invoke(this, EventArgs.Empty);
    }
    public long GetMaxSizeBytes()
    {
        if (!IsFileSizeLimitEnabled) return long.MaxValue;

        double multiplier = FileSizeLimitUnit switch
        {
            FileSizeUnit.Byte => 1,
            FileSizeUnit.KB => AppConstants.OneKB,
            FileSizeUnit.MB => AppConstants.OneMB,
            FileSizeUnit.GB => AppConstants.OneGB,
            FileSizeUnit.TB => AppConstants.OneTB,
            FileSizeUnit.PB => AppConstants.OnePB,
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