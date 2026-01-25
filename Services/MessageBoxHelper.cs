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
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        
        if (Application.Current != null && Application.Current.TryFindResource("PaneBackgroundBrush", null, out var bg) && bg is IBrush brush)
        {
            window.Background = brush;
        }

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10),
            TextAlignment = TextAlignment.Center
        };
        //* Ok button *//
        var button = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 100,
            Margin = new Thickness(0, 0, 0, 20)
        };
        
        
        // Close the window when the button is clicked
        button.Click += (_, _) => window.Close();

        var stackPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { textBlock, button }
        };

        window.Content = stackPanel;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await window.ShowDialog(desktop.MainWindow);
        }
    }

    public static async Task<bool> ShowConfirmationAsync(string title, string message, string yesText = "Yes", string noText = "No")
    {
        var window = new Window
        {
            Title = title,
            Width = 350,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        if (Application.Current != null && Application.Current.TryFindResource("PaneBackgroundBrush", null, out var bg) && bg is IBrush brush)
        {
            window.Background = brush;
        }

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 20, 10, 20),
            TextAlignment = TextAlignment.Center
        };

        var btnYes = new Button
        {
            Content = yesText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 80,
            Margin = new Thickness(5)
        };

        var btnNo = new Button
        {
            Content = noText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 80,
            Margin = new Thickness(5)
        };

        bool result = false;

        btnYes.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };

        btnNo.Click += (_, _) =>
        {
            result = false;
            window.Close();
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
            Children = { btnYes, btnNo }
        };

        var stackPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { textBlock, btnPanel }
        };

        window.Content = stackPanel;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
             await window.ShowDialog(desktop.MainWindow);
        }

        return result;
    }
}
