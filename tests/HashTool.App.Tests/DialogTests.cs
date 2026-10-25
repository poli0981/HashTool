using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HashTool.App.Infrastructure;
using HashTool.App.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

public sealed class DialogTests
{
    private static LocalizationService L => TestServices.Localization;

    private static Window ShowOwner()
    {
        var owner = new Window { Width = 800, Height = 600, FontSize = 17 };
        owner.Show();
        return owner;
    }

    /// <summary>The dialog the owner opened, once it is on screen.</summary>
    private static Window Dialog(Window owner)
    {
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        dialog.UpdateLayout();
        return dialog;
    }

    private static Button ButtonOf(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task Message_ClosesWithOk_AndCopiesItsDetails()
    {
        var owner = ShowOwner();
        try
        {
            var shown = DialogWindow.ShowAsync(owner, L, "Title", "Something failed", DialogIcon.Error, "stack trace");
            var dialog = Dialog(owner);
            Assert.Equal("Title", dialog.Title);
            Assert.Equal(17, dialog.FontSize);
            Assert.Equal(560, dialog.Width);

            Click(ButtonOf(dialog, L["Btn_CopyDetails"]));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Something failed\n\nstack trace", await dialog.Clipboard!.TryGetTextAsync());

            Click(ButtonOf(dialog, L["Btn_OK"]));
            await shown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_ReturnsTheAnswer(bool yes)
    {
        var owner = ShowOwner();
        try
        {
            var answer = DialogWindow.ConfirmAsync(owner, L, "Delete?", "Really?", DialogIcon.Question, "Yes!", "No!");
            Click(ButtonOf(Dialog(owner), yes ? "Yes!" : "No!"));

            Assert.Equal(yes, await answer.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task Escape_CancelsAConfirmation()
    {
        var owner = ShowOwner();
        try
        {
            var answer = DialogWindow.ConfirmAsync(owner, L, "Leave?", "Changes are lost.", DialogIcon.Warning, "Yes", "No");
            var dialog = Dialog(owner);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = dialog });

            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheTitleBar_StartsMovingTheWindow()
    {
        var owner = ShowOwner();
        try
        {
            var shown = DialogWindow.ShowAsync(owner, L, "Drag me", "Message", DialogIcon.Success);
            var dialog = Dialog(owner);
            var title = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Drag me");
            var point = title.TranslatePoint(new Point(2, 2), dialog)!.Value;
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);
            dialog.MouseDown(point, MouseButton.Right);

            Click(ButtonOf(dialog, L["Btn_OK"]));
            await shown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task WithoutAVisibleOwner_NothingIsShown()
    {
        foreach (var icon in Enum.GetValues<DialogIcon>())
            await DialogWindow.ShowAsync(null, L, "Title", "Message", icon);

        Assert.False(await DialogWindow.ConfirmAsync(new Window(), L, "Title", "Message", DialogIcon.None, "Yes", "No"));
    }

    [AvaloniaFact]
    public async Task DialogService_ShowsDialogsOverTheMainWindow()
    {
        var owner = ShowOwner();
        try
        {
            var service = new DialogService(new MainWindowAccessor(() => owner), L);

            var info = service.ShowAsync("Info", "Message");
            Click(ButtonOf(Dialog(owner), L["Btn_OK"]));
            await info.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            var error = service.ShowErrorAsync("Error", "Message", "details");
            Click(ButtonOf(Dialog(owner), L["Btn_OK"]));
            await error.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            var confirm = service.ConfirmAsync("Confirm", "Message");
            Click(ButtonOf(Dialog(owner), L["Btn_Yes"]));
            Assert.True(await confirm.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            owner.Close();
        }
    }
}

public sealed class PlatformServiceTests
{
    private static ILogger<LauncherService> Logger => TestServices.Loggers.CreateLogger<LauncherService>();

    [AvaloniaFact]
    public async Task WithoutAMainWindow_ServicesDoNothing()
    {
        var none = new MainWindowAccessor(() => null);

        Assert.Empty(await new StorageService(none).PickFilesAsync("t"));
        Assert.Empty(await new StorageService(none).PickFoldersAsync("t"));
        Assert.Null(await new StorageService(none).SaveFileAsync("t", "a.txt", [new FileTypeChoice("Text", "*.txt")]));
        await new ClipboardService(none).SetTextAsync("ignored");
        await new LauncherService(none, Logger).OpenUrlAsync("https://example.com");
        Assert.Null(new MainWindowAccessor().Window);
    }

    [AvaloniaFact]
    public async Task WithAWindow_ServicesUseThePlatform()
    {
        var window = new Window();
        window.Show();
        try
        {
            var main = new MainWindowAccessor(() => window);
            var storage = new StorageService(main);

            // The headless platform has no pickers: they come back empty, like a cancelled dialog.
            Assert.Empty(await storage.PickFilesAsync("t", allowMultiple: false, [new FileTypeChoice("Checksum", "*.sha256")]));
            Assert.Empty(await storage.PickFoldersAsync("t"));
            Assert.Null(await storage.SaveFileAsync("t", "SHA256SUMS.sha256", [new FileTypeChoice("GNU", "*.sha256")]));

            await new ClipboardService(main).SetTextAsync("copied");
            Assert.Equal("copied", await window.Clipboard!.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Launcher_OpensOnlyWebLinksAndDataFolders()
    {
        var window = new Window();
        window.Show();
        try
        {
            var launcher = new LauncherService(new MainWindowAccessor(() => window), Logger);
            var log = Directory.CreateDirectory(AppPaths.LogDirectory).FullName;

            await launcher.OpenUrlAsync("file:///etc/passwd");
            await launcher.OpenUrlAsync("not a url");
            await launcher.OpenUrlAsync("https://github.com/poli0981/HashTool");
            await launcher.OpenFolderAsync(Path.GetTempPath());
            await launcher.OpenFolderAsync(Path.Combine(log, "missing"));
            await launcher.OpenFolderAsync(log);

            var messages = AppHost.Get<Infrastructure.Logging.LogSink>().Snapshot().Select(e => e.Message).ToList();
            Assert.Contains(messages, m => m.StartsWith("Refused to open file:///etc/passwd", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.StartsWith("Refused to open folder", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.StartsWith("Could not open https://github.com/poli0981/HashTool", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }
}
