using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

public partial class CheckHashView : UserControl
{
    private CheckHashViewModel? _subscribed;

    public CheckHashView()
    {
        AvaloniaXamlLoader.Load(this);
        FileDrop.Attach(this.FindControl<Panel>("DropArea")!, this.FindControl<Border>("DropHighlight"), paths =>
            DataContext is CheckHashViewModel vm ? vm.AddPathsAsync(paths) : Task.CompletedTask);

        // A checksum file dropped onto a row's hash box fills in that row's expected hash.
        AddHandler(DragDrop.DropEvent, async (sender, e) =>
        {
            if (e.Source is not Control { DataContext: FileItem item } source || !IsHashInput(source)) return;
            var paths = FileDrop.Paths(e);
            if (paths.Count == 0 || DataContext is not CheckHashViewModel vm) return;
            e.Handled = true;
            await vm.LoadHashFileAsync(item, paths[0]);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static bool IsHashInput(Control control)
    {
        for (Control? c = control; c is not null; c = c.Parent as Control)
        {
            if (c is TextBox box && box.Classes.Contains("hashInput")) return true;
            if (c is ListBoxItem) return false;
        }

        return false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null) _subscribed.FocusSearchRequested -= OnFocusSearch;
        _subscribed = DataContext as CheckHashViewModel;
        if (_subscribed is not null) _subscribed.FocusSearchRequested += OnFocusSearch;
    }

    private void OnFocusSearch(object? sender, EventArgs e) => this.FindControl<TextBox>("SearchBox")?.Focus();
}
