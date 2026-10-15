using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using HashTool.App.Services;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

public partial class CheckHashView : UserControl
{
    public CheckHashView()
    {
        InitializeComponent();
    }

    private LocalizationService L => LocalizationService.Instance;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File) && sender is Control control)
        {
            control.Classes.Add("DragOver");
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control control)
        {
            control.Classes.Remove("DragOver");
        }
    }

    private async void OnHashFileDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control control)
        {
            control.Classes.Remove("DragOver");
        }

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null && files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;

                if (sender is TextBox textBox && textBox.DataContext is FileItem item)
                {
                    var hashFileName = Path.GetFileName(filePath);
                    if (!hashFileName.Contains(item.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = L["Status_DropHashMismatch"];
                        return;
                    }

                    try
                    {
                        string content;
                        using (var reader = new StreamReader(filePath))
                        {
                            var buffer = new char[5120]; // 5KB
                            var readCount = await reader.ReadAsync(buffer, 0, buffer.Length);
                            content = new string(buffer, 0, readCount);
                        }

                        var match = Regex.Match(content, @"[a-fA-F0-9]{32,128}");
                        if (match.Success)
                        {
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
    }

    private async void OnMainDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control control)
        {
            control.Classes.Remove("DragOver");
        }

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(x => x.Path.LocalPath).ToList();
            if (files == null || files.Count == 0) return;

            if (DataContext is FileListViewModelBase vm)
            {
                await vm.AddFilesFromPaths(files);
            }
        }
    }
}
