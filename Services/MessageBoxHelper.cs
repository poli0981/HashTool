using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace CheckHash.Services;

public enum MessageBoxIcon
{
    None,
    Information,
    Warning,
    Error,
    Success,
    Question
}

public static class MessageBoxHelper
{
    private static Control? GetIconControl(MessageBoxIcon icon)
    {
        if (icon == MessageBoxIcon.None) return null;

        MaterialIconKind kind;
        IBrush color;

        switch (icon)
        {
            case MessageBoxIcon.Information:
                kind = MaterialIconKind.Information;
                color = Brushes.DodgerBlue;
                break;
            case MessageBoxIcon.Success:
                kind = MaterialIconKind.CheckCircle;
                color = Brushes.Green;
                break;
            case MessageBoxIcon.Warning:
                kind = MaterialIconKind.Alert;
                color = Brushes.Orange;
                break;
            case MessageBoxIcon.Error:
                kind = MaterialIconKind.CloseCircle;
                color = Brushes.Red;
                break;
            case MessageBoxIcon.Question:
                kind = MaterialIconKind.HelpCircle;
                color = Brushes.DodgerBlue;
                break;
            default:
                return null;
        }

        return new MaterialIcon
        {
            Kind = kind,
            Width = 32,
            Height = 32,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
    }

    private static (Window window, StackPanel contentPanel) CreateBaseWindow(string title, MessageBoxIcon icon)
    {
        var window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            Width = 450,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None },
            Background = Brushes.Transparent
        };

        // Main Container (DockPanel)
        // Top: Title Bar
        // Center: Content (Icon + Text) + Buttons (Bottom of center?)
        // Actually, let's use a Grid for the whole window content
        // Row 0: Title Bar
        // Row 1: Content

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Title
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Content

        // Title Bar
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 8, 10, 8)
        };

        var titleBar = new Border
        {
            Child = titleText
        };

        // Drag logic
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                window.BeginMoveDrag(e);
            }
        };

        Grid.SetRow(titleBar, 0);
        rootGrid.Children.Add(titleBar);

        // Content Container
        var contentStack = new StackPanel
        {
            Margin = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetRow(contentStack, 1);
        rootGrid.Children.Add(contentStack);

        // Wrap everything in a Border to provide rounded corners and background
        var windowBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = rootGrid
        };

        // Bind resources if Application.Current is available
        if (Application.Current != null)
        {
            titleBar.Bind(Border.BackgroundProperty, Application.Current.GetResourceObservable("AppBackgroundBrush"));
            windowBorder.Bind(Border.BackgroundProperty, Application.Current.GetResourceObservable("PaneBackgroundBrush"));
            windowBorder.Bind(Border.BorderBrushProperty, Application.Current.GetResourceObservable("HighlightBrush"));
        }
        else
        {
            // Fallbacks for testing
            titleBar.Background = Brushes.LightGray;
            windowBorder.Background = Brushes.White;
            windowBorder.BorderBrush = Brushes.Gray;
        }

        window.Content = windowBorder;

        // Bind Fonts
        window.Bind(TemplatedControl.FontFamilyProperty, new Binding("SelectedFont") { Source = FontService.Instance });
        window.Bind(TemplatedControl.FontSizeProperty, new Binding("BaseFontSize") { Source = FontService.Instance });

        return (window, contentStack);
    }

    private static Grid CreateMessageContent(string message, MessageBoxIcon icon)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var iconControl = GetIconControl(icon);
        if (iconControl != null)
        {
            Grid.SetColumn(iconControl, 0);
            grid.Children.Add(iconControl);
        }

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left
        };

        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        return grid;
    }

    public static async Task ShowAsync(string title, string message, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        var (window, contentPanel) = CreateBaseWindow(title, icon);

        // Content
        var messageGrid = CreateMessageContent(message, icon);
        messageGrid.Margin = new Thickness(0, 0, 0, 20);
        contentPanel.Children.Add(messageGrid);

        // OK Button
        var button = new Button
        {
            Content = LocalizationService.Instance["Btn_OK"],
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Width = 80
        };
        button.Click += (_, _) => window.Close();

        var btnContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { button }
        };
        contentPanel.Children.Add(btnContainer);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            await window.ShowDialog(desktop.MainWindow);
    }

    public static async Task<bool> ShowConfirmationAsync(string title, string message, string yesText = "Yes",
        string noText = "No", MessageBoxIcon icon = MessageBoxIcon.Question)
    {
        var (window, contentPanel) = CreateBaseWindow(title, icon);

        // Content
        var messageGrid = CreateMessageContent(message, icon);
        messageGrid.Margin = new Thickness(0, 0, 0, 20);
        contentPanel.Children.Add(messageGrid);

        // Buttons
        var btnYes = new Button
        {
            Content = yesText,
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var btnNo = new Button
        {
            Content = noText,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Width = 80
        };

        var result = false;

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
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { btnYes, btnNo }
        };
        contentPanel.Children.Add(btnPanel);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            await window.ShowDialog(desktop.MainWindow);

        return result;
    }

    public static async Task<(bool Confirmed, bool IsChecked)> ShowConfirmationWithCheckboxAsync(
        string title, string message, string checkboxText, string yesText = "Yes", string noText = "No", MessageBoxIcon icon = MessageBoxIcon.Question)
    {
        var (window, contentPanel) = CreateBaseWindow(title, icon);

        // Content
        var messageGrid = CreateMessageContent(message, icon);
        messageGrid.Margin = new Thickness(0, 0, 0, 10);
        contentPanel.Children.Add(messageGrid);

        // Checkbox
        var checkBox = new CheckBox
        {
            Content = checkboxText,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 20)
        };

        contentPanel.Children.Add(checkBox);

        // Buttons
        var btnYes = new Button
        {
            Content = yesText,
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var btnNo = new Button
        {
            Content = noText,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Width = 80
        };

        var confirmed = false;

        btnYes.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };

        btnNo.Click += (_, _) =>
        {
            confirmed = false;
            window.Close();
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { btnYes, btnNo }
        };
        contentPanel.Children.Add(btnPanel);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
            await window.ShowDialog(desktop.MainWindow);

        return (confirmed, checkBox.IsChecked ?? false);
    }
}
