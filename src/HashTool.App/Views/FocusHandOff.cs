using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HashTool.App.Views.Controls;

/// <summary>
/// Moves keyboard focus to <see cref="TargetProperty"/> when the focused control hides itself, such as COMPUTE ALL turning
/// into Cancel while a run starts, so keyboard and screen reader users keep their place instead of losing focus.
/// </summary>
public static class FocusHandOff
{
    public static readonly AttachedProperty<Control?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("Target", typeof(FocusHandOff));

    static FocusHandOff() => TargetProperty.Changed.AddClassHandler<Control>(OnTargetChanged);

    public static Control? GetTarget(Control control) => control.GetValue(TargetProperty);

    public static void SetTarget(Control control, Control? value) => control.SetValue(TargetProperty, value);

    private static void OnTargetChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        if (e.NewValue is not null) control.AddHandler(InputElement.LostFocusEvent, OnLostFocus);
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // Only when the control lost focus because it disappeared, not because the user moved on.
        if (sender is not Control control || !ReferenceEquals(e.Source, control) || control.IsEffectivelyVisible) return;
        if (GetTarget(control) is not { } target || TopLevel.GetTopLevel(control) is not { } topLevel) return;

        // The target usually becomes visible in the same update, possibly after this control was hidden.
        Dispatcher.UIThread.Post(() =>
        {
            var focused = topLevel.FocusManager?.GetFocusedElement();
            if ((focused is null || ReferenceEquals(focused, topLevel)) && ReferenceEquals(TopLevel.GetTopLevel(target), topLevel) &&
                target.IsEffectivelyVisible && target.IsEffectivelyEnabled)
                target.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Input);
    }
}
