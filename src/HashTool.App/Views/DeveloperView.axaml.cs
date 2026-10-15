using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HashTool.App.ViewModels;

namespace HashTool.App.Views;

public partial class DeveloperView : UserControl
{
    private DeveloperViewModel? _subscribed;

    public DeveloperView() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null) _subscribed.EntriesAppended -= OnEntriesAppended;
        _subscribed = DataContext as DeveloperViewModel;
        if (_subscribed is not null) _subscribed.EntriesAppended += OnEntriesAppended;
    }

    private void OnEntriesAppended(object? sender, EventArgs e)
    {
        if (this.FindControl<ListBox>("LogList") is not { ItemCount: > 0 } list) return;
        // Scroll after layout so the new rows exist.
        Dispatcher.UIThread.Post(() => list.ScrollIntoView(list.ItemCount - 1), DispatcherPriority.Background);
    }
}
