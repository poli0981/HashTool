using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HashTool.App.Views.Controls;

/// <summary>
/// Lets Tab reach the buttons of a list row. A <see cref="ListBox"/> is a single tab stop (the arrow keys move between
/// rows), so without this the controls inside a row only work with a mouse. Tab from a row, or from one of its controls,
/// moves to the row's next control and leaves the list after the last one; Shift+Tab walks back to the row itself.
/// </summary>
public static class RowTabNavigation
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("IsEnabled", typeof(RowTabNavigation));

    static RowTabNavigation() => IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);

    public static bool GetIsEnabled(ListBox list) => list.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ListBox list, bool value) => list.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ListBox list, AvaloniaPropertyChangedEventArgs e)
    {
        list.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (e.NewValue is true) list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return;
        if (TopLevel.GetTopLevel(sender as Visual)?.FocusManager?.GetFocusedElement() is not Control focused) return;
        var row = focused as ListBoxItem ?? focused.FindAncestorOfType<ListBoxItem>();
        if (row is null) return;

        var stops = TabStops(row);
        var index = stops.FindIndex(stop => stop == focused || stop.IsVisualAncestorOf(focused));
        Control? next;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            next = index switch
            {
                < 0 => null, // on the row itself: leave the list backwards as usual
                0 => row,
                _ => stops[index - 1]
            };
        else
            next = index + 1 < stops.Count ? stops[index + 1] : null; // after the last control: leave the list as usual

        if (next is null) return;
        next.Focus(NavigationMethod.Tab, e.KeyModifiers);
        e.Handled = true;
    }

    /// <summary>The row's own controls that take keyboard focus, in layout order; not the parts inside their templates.</summary>
    private static List<Control> TabStops(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled &&
                        KeyboardNavigation.GetIsTabStop(c))
            .ToList();
}
