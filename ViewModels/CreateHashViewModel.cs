using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckHash.Services;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace CheckHash.ViewModels;

public partial class FileItem : ObservableObject
{
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _status = "Sẵn sàng";
    [ObservableProperty] private string _resultHash = "";
    [ObservableProperty] private bool _isProcessing;
    
    // Dùng cho Tạo Hash
    [ObservableProperty] private HashType _selectedAlgorithm = HashType.SHA256;

    // Dùng cho Check Hash
    [ObservableProperty] private string _expectedHash = ""; 
    [ObservableProperty] private bool? _isMatch;
}

public partial class CreateHashViewModel : ObservableObject
{
    private readonly HashService _hashService = new();
    public string TotalFilesText => $"Tổng số file: {Files.Count}";
    
    public ObservableCollection<FileItem> Files { get; } = new();

    public ObservableCollection<HashType> AlgorithmList { get; } = new(Enum.GetValues<HashType>());
    
    private bool CanComputeAll => Files.Count > 0;

    [RelayCommand]
    private async Task AddFiles(Avalonia.Controls.Window window)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Chọn các file cần Hash"
        });

        if (files.Count > 0)
        {
            foreach (var file in files)
            {
                if (!Files.Any(f => f.FilePath == file.Path.LocalPath))
                {
                    Files.Add(new FileItem
                    {
                        FileName = file.Name,
                        FilePath = file.Path.LocalPath,
                        SelectedAlgorithm = HashType.SHA256 // Mặc định
                    });
                }
            }

            // Báo cho lệnh ComputeAllCommand biết trạng thái đã thay đổi -> Sáng nút lên
            ComputeAllCommand.NotifyCanExecuteChanged();
        }
        OnPropertyChanged(nameof(TotalFilesText));
        ComputeAllCommand.NotifyCanExecuteChanged();
    }
    
    
    [RelayCommand]
    private async Task CompressFiles(Avalonia.Controls.Window window)
    {
        if (Files.Count == 0) return;

        var fileSave = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Lưu file nén",
            SuggestedFileName = $"Archive_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ZIP Archive")
                {
                    Patterns = new[] { "*.zip" },
                    MimeTypes = new[] { "application/zip"}
                },
                new FilePickerFileType("Gzip Archive")
                {
                    Patterns = new[] { "*.tar.gz","*.tgz" },
                    MimeTypes = new[] { "application/gzip"}
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (fileSave != null)
        {
            try
            {
                var zipPath = fileSave.Path.LocalPath;

                await Task.Run(() =>
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);

                    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    foreach (var item in Files)
                    {
                        if (File.Exists(item.FilePath))
                        {
                            // Thêm file vào zip. item.FileName là tên hiển thị trong zip
                            archive.CreateEntryFromFile(item.FilePath, item.FileName);
                        }
                    }
                });
                
                foreach (var f in Files) f.Status = "Đã nén xong!";
            }
            catch (Exception ex)
            {
                // Xử lý lỗi (ví dụ file đang được mở bởi app khác)
                await MessageBoxHelper.ShowAsync("Lỗi nén file", 
                    $"Không thể tạo file nén:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void ClearList()
    {
        Files.Clear();
        OnPropertyChanged(nameof(TotalFilesText));
        ComputeAllCommand.NotifyCanExecuteChanged(); // List rỗng -> Disable nút
    }

    [RelayCommand(CanExecute = nameof(CanComputeAll))]
    private async Task ComputeAll()
    {
        int successCount = 0;
        int failCount = 0;

        foreach (var file in Files)
        {
            file.IsProcessing = true;
            file.Status = $"Đang tính ({file.SelectedAlgorithm})..."; 
            try
            {
                // Kiểm tra file tồn tại trước khi tính hash
                if (!File.Exists(file.FilePath))
                {
                    file.Status = "Lỗi: File không tồn tại";
                    failCount++;
                    continue;
                }

                file.ResultHash = await _hashService.ComputeHashAsync(file.FilePath, file.SelectedAlgorithm, CancellationToken.None);
                file.Status = "Hoàn tất";
                successCount++;
            }
            catch (FileNotFoundException)
            {
                file.Status = "Lỗi: File không tìm thấy";
                failCount++;
            }
            catch (UnauthorizedAccessException)
            {
                file.Status = "Lỗi: Không có quyền truy cập";
                failCount++;
            }
            catch (IOException)
            {
                file.Status = "Lỗi: File đang được sử dụng";
                failCount++;
            }
            catch (Exception ex)
            {
                file.Status = "Lỗi: " + ex.Message;
                failCount++;
            }
            finally
            {
                file.IsProcessing = false;
            }
        }

        // Hiện thông báo tổng kết
        await MessageBoxHelper.ShowAsync("Kết quả", 
            $"Đã xử lý xong!\n\n✅ Thành công: {successCount}\n❌ Thất bại: {failCount}");
    }

    [RelayCommand]
    private async Task SaveHashFile(FileItem item)
    {
        if (string.IsNullOrEmpty(item.ResultHash)) return;

        // Lưu đuôi file theo thuật toán của item đó
        var ext = item.SelectedAlgorithm.ToString().ToLower();

        var window =
            Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Lưu file Hash",
            SuggestedFileName = $"{item.FileName}.{ext}",
            DefaultExtension = ext
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(item.ResultHash);
            item.Status = "Đã lưu!";
        }
    }
    
    [RelayCommand]
    private async Task CopyToClipboard(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return;

        // Lấy Clipboard từ ApplicationLifetime (Hỗ trợ cả Windows/Mac)
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(hash);
                // Có thể hiện thông báo nhỏ "Copied" nếu muốn (Optional)
            }
        }
    }
    // 1. Tính năng mới: Xóa 1 file khỏi list
    [RelayCommand]
    private void RemoveFile(FileItem item)
    {
        if (Files.Contains(item))
        {
            Files.Remove(item);
            OnPropertyChanged(nameof(TotalFilesText)); // Cập nhật số lượng
            ComputeAllCommand.NotifyCanExecuteChanged();
        }
    }

    // 2. Tính năng mới: Tính Hash cho 1 file duy nhất
    [RelayCommand]
    private async Task ComputeSingle(FileItem item)
    {
        item.IsProcessing = true;
        item.Status = $"Đang tính ({item.SelectedAlgorithm})...";
        try
        {
            // Kiểm tra file tồn tại trước khi tính hash
            if (!File.Exists(item.FilePath))
            {
                item.Status = "Lỗi: File không tồn tại";
                await MessageBoxHelper.ShowAsync("Lỗi", $"File không tồn tại:\n{item.FilePath}");
                return;
            }

            item.ResultHash = await _hashService.ComputeHashAsync(item.FilePath, item.SelectedAlgorithm, CancellationToken.None);
            item.Status = "Hoàn tất";
        }
        catch (FileNotFoundException ex)
        {
            item.Status = "Lỗi: File không tìm thấy";
            await MessageBoxHelper.ShowAsync("Lỗi", $"Không tìm thấy file:\n{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            item.Status = "Lỗi: Không có quyền truy cập";
            await MessageBoxHelper.ShowAsync("Lỗi", $"Không có quyền truy cập file:\n{ex.Message}");
        }
        catch (IOException ex)
        {
            item.Status = "Lỗi: File đang được sử dụng";
            await MessageBoxHelper.ShowAsync("Lỗi", $"Không thể đọc file (có thể đang được sử dụng):\n{ex.Message}");
        }
        catch (Exception ex)
        {
            item.Status = "Lỗi: " + ex.Message;
            await MessageBoxHelper.ShowAsync("Lỗi", $"Lỗi không xác định:\n{ex.Message}");
        }
        finally
        {
            item.IsProcessing = false;
        }
    }
}