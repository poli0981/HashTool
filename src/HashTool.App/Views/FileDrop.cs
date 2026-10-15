using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace HashTool.App.Views;

/// <summary>Accepts files and folders dropped from the file manager onto a control.</summary>
internal static class FileDrop
{
    public static void Attach(Control target, Control? highlight, Func<IReadOnlyList<string>, Task> onDrop)
    {
        target.AddHandler(DragDrop.DragEnterEvent, (_, e) => Update(e, highlight));
        target.AddHandler(DragDrop.DragOverEvent, (_, e) => Update(e, highlight));
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetHighlight(highlight, false));
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            SetHighlight(highlight, false);
            var paths = Paths(e);
            if (paths.Count == 0) return;
            e.Handled = true;
            await onDrop(paths);
        });
    }

    public static IReadOnlyList<string> Paths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    private static void Update(DragEventArgs e, Control? highlight)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetHighlight(highlight, hasFiles);
    }

    private static void SetHighlight(Control? highlight, bool visible)
    {
        if (highlight is not null) highlight.IsVisible = visible;
    }
}
