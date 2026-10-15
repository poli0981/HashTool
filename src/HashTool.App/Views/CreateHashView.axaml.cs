using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

public partial class CreateHashView : UserControl
{
    private CreateHashViewModel? _subscribed;

    public CreateHashView()
    {
        AvaloniaXamlLoader.Load(this);
        var dropArea = this.FindControl<Panel>("DropArea")!;
        FileDrop.Attach(dropArea, this.FindControl<Border>("DropHighlight"), paths =>
            DataContext is CreateHashViewModel vm ? vm.AddPathsAsync(paths) : System.Threading.Tasks.Task.CompletedTask);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null) _subscribed.FocusSearchRequested -= OnFocusSearch;
        _subscribed = DataContext as CreateHashViewModel;
        if (_subscribed is not null) _subscribed.FocusSearchRequested += OnFocusSearch;
    }

    private void OnFocusSearch(object? sender, EventArgs e) => this.FindControl<TextBox>("SearchBox")?.Focus();
}
