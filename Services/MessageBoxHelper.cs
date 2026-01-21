using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;

namespace CheckHash.Services;

public static class MessageBoxHelper
{
    public static async Task ShowAsync(string title, string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 300,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly, // Giao diện đơn giản
            Background = Brushes.Black // Hoặc lấy theo Theme nếu muốn kỹ hơn
        };

        // Lấy theme background hiện tại (nếu có) để không bị trắng toát trong dark mode
        if (Application.Current?.TryFindResource("PaneBackgroundBrush", null, out var bg) == true && bg is IBrush brush)
        {
            window.Background = brush;
        }

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };

        var button = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 100
        };
        button.Click += (_, _) => window.Close();

        var stackPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20,
            Children = { textBlock, button }
        };

        window.Content = stackPanel;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            await window.ShowDialog(desktop.MainWindow);
        }
    }
}