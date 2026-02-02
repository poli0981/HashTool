using System.Threading;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.ViewModels;

public partial class FileItem : ObservableObject
{
    // Check Hash
    [ObservableProperty] private string _expectedHash = "";

    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileSize = "";
    [ObservableProperty] private bool? _isMatch;
    [ObservableProperty] private bool _isProcessing;

    // Reveal/Hide Expected Hash
    [ObservableProperty] private bool _isRevealed;
    [ObservableProperty] private string _processDuration = "";
    [ObservableProperty] private string _resultHash = "";

    // Algorithm Select
    [ObservableProperty] private HashType _selectedAlgorithm = HashType.SHA256;
    [ObservableProperty] private bool _hasSpecificAlgorithm;
    [ObservableProperty] private string _status;

    public FileItem()
    {
        _status = L["Lbl_Status_Ready"];
    }

    private LocalizationService L => LocalizationService.Instance;

    public CancellationTokenSource? Cts { get; set; }
    public bool IsDeleted { get; set; }

    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}