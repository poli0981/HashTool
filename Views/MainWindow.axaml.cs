using System;
using Avalonia.Controls;
using Avalonia.Input;
using CheckHash.ViewModels;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using CheckHash.Services;

namespace CheckHash.Views;

public partial class MainWindow : Window
{
    private LocalizationService L => LocalizationService.Instance;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Sự kiện khi Kéo file vào TextBox
    private async void OnHashFileDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        // Check if dropped data contains files
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files != null && files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
            
                // 2. Lấy TextBox và DataContext (FileItem)
                if (sender is TextBox textBox && textBox.DataContext is FileItem item)
                {
                    // Sử dụng System.IO.Path rõ ràng để tránh conflict với Avalonia.Controls.Path
                    var hashFileName = System.IO.Path.GetFileName(filePath);
                    if (!hashFileName.Contains(item.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = L["Status_DropHashMismatch"];
                        return;
                    }

                    try
                    {
                        // 3. Đọc nội dung file
                        var content = await File.ReadAllTextAsync(filePath);
                    
                        // 4. Lọc lấy mã Hash
                        var match = Regex.Match(content, @"[a-fA-F0-9]{32,128}");
                        if (match.Success)
                        {
                            // Gán vào TextBox (thông qua ViewModel để trigger Binding)
                            item.ExpectedHash = match.Value;
                            item.Status = L["Status_DropHashSuccess"];
                        }
                        else
                        {
                            item.Status = L["Status_DropNoHash"];
                        }
                    }
                    catch
                    {
                        item.Status = L["Status_ReadError"];
                    }
                }
            }
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}