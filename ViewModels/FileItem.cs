using CommunityToolkit.Mvvm.ComponentModel;
using CheckHash.Services;
using System.Threading;

namespace CheckHash.ViewModels;

public partial class FileItem : ObservableObject
{
    private LocalizationService L => LocalizationService.Instance;

    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileSize = "";
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _resultHash = "";
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _processDuration = "";

    // Algorithm Select
    [ObservableProperty] private HashType _selectedAlgorithm = HashType.SHA256;

    // Check Hash
    [ObservableProperty] private string _expectedHash = "";
    [ObservableProperty] private bool? _isMatch;
    
    // Reveal/Hide Expected Hash
    [ObservableProperty] private bool _isRevealed;

    public CancellationTokenSource? Cts { get; set; }

    public FileItem()
    {
        _status = L["Lbl_Status_Ready"];
    }

    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}