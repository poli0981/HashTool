using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HashTool.App.ViewModels;
using HashTool.App.Views;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>Files dropped from the file manager onto the pages, through Avalonia's real drag and drop routing.</summary>
public sealed class DropTests
{
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    private static void Drop(TopLevel window, Point point, IDataTransfer data)
    {
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
    }

    private static Window Show(Control view)
    {
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [AvaloniaFact]
    public async Task HashFileDroppedOnARow_FillsThatRowInsteadOfBeingAdded()
    {
        using var temp = new TempDirectory();
        var file = temp.File("data.bin", "hello");
        var hashFile = temp.File("expected.txt", HelloSha256);
        var vm = new CheckHashViewModel(TestServices.Create());
        await vm.AddPathsAsync([file]);
        var view = new CheckHashView { DataContext = vm };
        var window = Show(view);
        try
        {
            var box = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("hashInput"));
            Drop(window, box.TranslatePoint(new Point(4, 4), window)!.Value, await Drops.FilesAsync(window, hashFile));

            var row = Assert.Single(vm.Items);
            await Wait.UntilAsync(() => !string.IsNullOrEmpty(row.ExpectedHash));
            Assert.Equal(HelloSha256, row.ExpectedHash, ignoreCase: true);
            Assert.Equal(ItemStatus.HashLoaded, row.Status);
            Assert.Single(vm.Items);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FilesDroppedOnTheList_AreAdded()
    {
        using var temp = new TempDirectory();
        var file = temp.File("data.bin", "hello");
        var vm = new CreateHashViewModel(TestServices.Create());
        var view = new CreateHashView { DataContext = vm };
        var window = Show(view);
        try
        {
            Drop(window, new Point(600, 500), await Drops.FilesAsync(window, file));

            await Wait.UntilAsync(() => vm.Items.Count == 1);
            Assert.Equal(file, vm.Items[0].FilePath);
        }
        finally
        {
            window.Close();
        }
    }
}
