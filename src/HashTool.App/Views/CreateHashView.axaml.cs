using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

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
