using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace HashTool.App.Services;

/// <summary>
/// Themed message box: keyboard friendly (Enter = primary button, Escape = cancel), follows the UI language's reading
/// direction, scrolls long content and lets users select or copy the text.
/// </summary>
internal static class DialogWindow
{
    public static async Task ShowAsync(Window? owner, LocalizationService localization, string title, string message,
        DialogIcon icon, string? details = null)
    {
        var (window, buttons) = Create(owner, localization, title, message, icon, details);
        var ok = Button(localization["Btn_OK"], isDefault: true, isCancel: true);
        ok.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        await ShowDialog(window, owner, ok);
    }

    public static async Task<bool> ConfirmAsync(Window? owner, LocalizationService localization, string title, string message,
        DialogIcon icon, string yesText, string noText)
    {
        var (window, buttons) = Create(owner, localization, title, message, icon, null);
        var result = false;
        var yes = Button(yesText, isDefault: true, isCancel: false);
        var no = Button(noText, isDefault: false, isCancel: true);
        yes.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };
        no.Click += (_, _) => window.Close();
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        await ShowDialog(window, owner, yes);
        return result;
    }

    private static async Task ShowDialog(Window window, Window? owner, Control focus)
    {
        window.Opened += (_, _) => focus.Focus();
        if (owner is { IsVisible: true }) await window.ShowDialog(owner);
    }

    private static (Window Window, StackPanel Buttons) Create(Window? owner, LocalizationService localization, string title,
        string message, DialogIcon icon, string? details)
    {
        var window = new Window
        {
            Title = title,
            WindowDecorations = WindowDecorations.None,
            Width = details is null ? 460 : 560,
            MaxHeight = 640,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None],
            FlowDirection = localization.FlowDirection
        };
        if (owner is not null)
        {
            window.FontFamily = owner.FontFamily;
            window.FontSize = owner.FontSize;
        }

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };

        var titleBar = new Border
        {
            Padding = new Thickness(14, 10),
            Child = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) window.BeginMoveDrag(e);
        };
        titleBar.Bind(Border.BackgroundProperty, window.GetResourceObservable("AppBackgroundBrush"));

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(20, 16, 20, 8) };
        if (IconFor(icon) is { } iconControl) body.Children.Add(iconControl);
        var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var scroller = new ScrollViewer { Content = text, MaxHeight = 380, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroller, 1);
        body.Children.Add(scroller);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 8, 20, 16)
        };
        if (details is not null)
        {
            var copy = Button(localization["Btn_CopyDetails"], isDefault: false, isCancel: false);
            copy.Click += async (_, _) =>
            {
                if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync($"{message}\n\n{details}");
            };
            buttons.Children.Add(copy);
        }

        var layout = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(titleBar);
        layout.Children.Add(buttons);
        layout.Children.Add(body);

        var frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true, Child = layout };
        frame.Bind(Border.BackgroundProperty, window.GetResourceObservable("PaneBackgroundBrush"));
        frame.Bind(Border.BorderBrushProperty, window.GetResourceObservable("HighlightBrush"));
        window.Content = frame;
        return (window, buttons);
    }

    private static Button Button(string text, bool isDefault, bool isCancel) => new()
    {
        Content = text,
        MinWidth = 96,
        Padding = new Thickness(16, 6),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        IsDefault = isDefault,
        IsCancel = isCancel
    };

    private static MaterialIcon? IconFor(DialogIcon icon)
    {
        (MaterialIconKind Kind, string BrushKey)? spec = icon switch
        {
            DialogIcon.Information => (MaterialIconKind.Information, "StatusInfoBrush"),
            DialogIcon.Success => (MaterialIconKind.CheckCircle, "StatusSuccessBrush"),
            DialogIcon.Warning => (MaterialIconKind.Alert, "StatusWarningBrush"),
            DialogIcon.Error => (MaterialIconKind.CloseCircle, "StatusErrorBrush"),
            DialogIcon.Question => (MaterialIconKind.HelpCircle, "StatusInfoBrush"),
            _ => null
        };
        if (spec is not { } chosen) return null;
        var (kind, brushKey) = chosen;
        var control = new MaterialIcon
        {
            Kind = kind,
            Width = 32,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0)
        };
        control.Bind(TemplatedControl.ForegroundProperty, control.GetResourceObservable(brushKey));
        return control;
    }
}
