using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CheckHash.Services;
using CheckHash.ViewModels;

namespace CheckHash.Views;

public partial class CreateHashView : UserControl
{
    public CreateHashView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) && sender is Control control)
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

    private async void OnMainDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control control)
        {
            control.Classes.Remove("DragOver");
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles()?.Select(x => x.Path.LocalPath).ToList();
            if (files == null || files.Count == 0) return;

            if (DataContext is FileListViewModelBase vm)
            {
                await vm.AddFilesFromPaths(files);
            }
        }
    }
}