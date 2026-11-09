using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.App.ViewModels;
using HashTool.App.Views;
using HashTool.Core.Configuration;
using HashTool.Core.Tests;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>
/// What screen readers and keyboard users get: every control has a real, translated name, Tab reaches the buttons of a
/// file row, focus survives buttons that swap places, and text colours are readable on every theme.
/// </summary>
[Collection(SharedStateCollection.Name)]
public sealed class AccessibilityTests
{
    private static readonly string[] Pages = ["Create", "Check", "Settings", "Update", "About", "Developer"];

    /// <summary>A shown main window with two files on Generate and Verify; cleans up after itself.</summary>
    private sealed class Screen : IAsyncDisposable
    {
        private readonly TempDirectory _temp = new();

        public MainWindowViewModel Main { get; } = AppHost.Get<MainWindowViewModel>();
        public CreateHashViewModel Create { get; } = AppHost.Get<CreateHashViewModel>();
        public CheckHashViewModel Check { get; } = AppHost.Get<CheckHashViewModel>();
        public LocalizationService Localization { get; } = AppHost.Get<LocalizationService>();
        public MainWindow Window { get; private set; } = null!;

        public static async Task<Screen> ShowAsync()
        {
            var screen = new Screen();
            await screen.Create.ClearListCommand.ExecuteAsync(null);
            await screen.Check.ClearListCommand.ExecuteAsync(null);
            string[] files = [screen._temp.File("alpha.txt", "123456789"), screen._temp.File("beta.txt", "abc")];
            await screen.Create.AddPathsAsync(files);
            await screen.Check.AddPathsAsync(files);
            screen.Window = new MainWindow { DataContext = screen.Main };
            screen.Window.Show();
            return screen;
        }

        public void Navigate(string page)
        {
            Main.NavigateCommand.Execute(page);
            Settle();
        }

        public void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public IInputElement? Focused => Window.FocusManager?.GetFocusedElement();

        public ListBox FileList() => Window.GetVisualDescendants().OfType<ListBox>().First(l => l.IsEffectivelyVisible);

        public async ValueTask DisposeAsync()
        {
            Localization.SelectedLanguage = Localization.AvailableLanguages[0];
            Main.NavigateCommand.Execute("Create");
            Window.Close();
            await Create.ClearListCommand.ExecuteAsync(null);
            await Check.ClearListCommand.ExecuteAsync(null);
            _temp.Dispose();
        }
    }

    private static bool IsInteractive(Control control) =>
        control is Button or ToggleButton or ComboBox or TextBox or NumericUpDown or Slider or ListBox or ListBoxItem
            or ProgressBar;

    /// <summary>
    /// Not empty, not an untranslated key, and not a type name a screen reader would read out, such as
    /// "Avalonia.Controls.StackPanel" for a button showing an icon and a label, or a row's view model.
    /// </summary>
    private static bool IsMeaningful(Control control, string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.StartsWith("Avalonia.", StringComparison.Ordinal) &&
        name != (control as ContentControl)?.Content?.GetType().FullName &&
        name != control.DataContext?.GetType().FullName &&
        !(name.StartsWith('[') && name.EndsWith(']'));

    private static string Describe(Control control)
    {
        var path = control.GetVisualAncestors().OfType<Control>().Select(c => c.GetType().Name).Take(4).Reverse();
        return $"{string.Join(" > ", path)} > {control.GetType().Name}";
    }

    [AvaloniaFact]
    public async Task EveryControlHasATranslatedAccessibleName()
    {
        await using var screen = await Screen.ShowAsync();
        var problems = new List<string>();
        foreach (var code in new[] { "en-US", "vi-VN" })
        {
            screen.Localization.SelectedLanguage = screen.Localization.Find(code);
            foreach (var page in Pages)
            {
                screen.Navigate(page);
                // Parts of a control's template count when the keyboard reaches them (a NumericUpDown's text box) and
                // screen readers see them.
                var controls = screen.Window.GetVisualDescendants().OfType<Control>()
                    .Where(c => IsInteractive(c) && (c.TemplatedParent is null ||
                        (c.IsEffectivelyVisible && c.Focusable && KeyboardNavigation.GetIsTabStop(c) &&
                         AutomationProperties.GetAccessibilityView(c) != AccessibilityView.Raw)));
                foreach (var control in controls)
                {
                    var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();
                    if (!IsMeaningful(control, name)) problems.Add($"{code}/{page}: {Describe(control)} is named '{name}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact]
    public async Task PageTitlesAndSectionsAreHeadings()
    {
        await using var screen = await Screen.ShowAsync();
        foreach (var page in Pages)
        {
            screen.Navigate(page);
            var headings = screen.Window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(t).GetHeadingLevel() > 0)
                .ToList();
            Assert.True(headings.Count(t => ControlAutomationPeer.CreatePeerForElement(t).GetHeadingLevel() == 1) == 1,
                $"{page}: {headings.Count} headings");
        }
    }

    [AvaloniaTheory]
    [InlineData("Create")]
    [InlineData("Check")]
    public async Task Tab_MovesThroughTheControlsOfTheFocusedRow(string page)
    {
        await using var screen = await Screen.ShowAsync();
        screen.Navigate(page);
        var list = screen.FileList();
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
        row.Focus(NavigationMethod.Tab);
        screen.Settle();

        var visited = new List<Control>();
        for (var i = 0; i < 20; i++)
        {
            screen.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            screen.Settle();
            if (screen.Focused is not Control focused || !row.IsVisualAncestorOf(focused)) break;
            visited.Add(focused);
        }

        // Generate: algorithm, copy, save, compute, clear, remove. Verify: expected hash, browse, verify, clear, remove.
        Assert.True(visited.Count >= 4, $"Tab reached {visited.Count} controls of the row");
        Assert.Contains(visited, c => c is Button button && AutomationProperties.GetName(button) == screen.Localization["Tip_RemoveFile"]);
        Assert.False(screen.Focused is Control after && list.IsVisualAncestorOf(after), "Tab after the last control leaves the list");

        // Shift+Tab walks back through the row to the row itself, and from there out of the list.
        void ShiftTab()
        {
            screen.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            screen.Settle();
        }

        visited[1].Focus(NavigationMethod.Tab);
        ShiftTab();
        Assert.Same(visited[0], screen.Focused);
        ShiftTab();
        Assert.Same(row, screen.Focused);
        ShiftTab();
        Assert.False(screen.Focused is Control before && list.IsVisualAncestorOf(before), "Shift+Tab from the row leaves the list");
    }

    [AvaloniaTheory]
    [InlineData("Create")]
    [InlineData("Check")]
    public async Task Focus_MovesToCancel_WhenRunAllHidesItself(string page)
    {
        await using var screen = await Screen.ShowAsync();
        FileListViewModelBase list = page == "Create" ? screen.Create : screen.Check;
        screen.Navigate(page);
        var buttons = screen.Window.GetVisualDescendants().OfType<Button>().ToList();
        var runAll = buttons.Single(b => b.Command == list.RunAllCommand);
        var cancel = buttons.Single(b => b.Command == list.CancelCommand);
        runAll.Focus(NavigationMethod.Tab);

        list.IsBusy = true;
        screen.Settle();
        Assert.Same(cancel, screen.Focused);

        list.IsBusy = false;
        screen.Settle();
        Assert.Same(runAll, screen.Focused);

        // Moving on by choice is left alone: hiding a control that no longer has focus moves nothing.
        var search = screen.Window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SearchBox");
        search.Focus(NavigationMethod.Tab);
        list.IsBusy = true;
        screen.Settle();
        Assert.Same(search, screen.Focused);
        list.IsBusy = false;
    }

    private static Color Opaque(IBrush brush, Color under)
    {
        var color = ((ISolidColorBrush)brush).Color;
        var a = color.A / 255.0;
        return Color.FromRgb((byte)Math.Round(color.R * a + under.R * (1 - a)), (byte)Math.Round(color.G * a + under.G * (1 - a)),
            (byte)Math.Round(color.B * a + under.B * (1 - a)));
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    internal static double Contrast(Color a, Color b)
    {
        var (light, dark) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (light + 0.05) / (dark + 0.05);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextColoursReachWcagAA_OnEveryTheme(bool isDark)
    {
        // Status texts sit on the page (AppBackgroundBrush) and need 4.5:1; on cards, banners, dialogs and the navigation pane
        // (PaneBackgroundBrush) status colours only colour icons, which need 3:1. Secondary text appears on both.
        string[] status = ["StatusSuccessBrush", "StatusErrorBrush", "StatusWarningBrush", "StatusInfoBrush"];
        (string Text, string Background, double Minimum)[] pairs =
        [
            .. status.Select(key => (key, "AppBackgroundBrush", 4.5)),
            .. status.Select(key => (key, "PaneBackgroundBrush", 3.0)),
            ("SecondaryTextBrush", "AppBackgroundBrush", 4.5),
            ("SecondaryTextBrush", "PaneBackgroundBrush", 4.5)
        ];
        var problems = new List<string>();
        foreach (var style in Enum.GetValues<AppThemeStyle>())
        {
            var palette = ThemePalettes.Get(style, isDark);
            var window = isDark ? Colors.Black : Colors.White;
            foreach (var (textKey, backgroundKey, minimum) in pairs)
            {
                var background = Opaque((IBrush)palette[backgroundKey], window);
                var ratio = Contrast(Opaque((IBrush)palette[textKey], background), background);
                if (ratio < minimum) problems.Add($"{style}/{(isDark ? "dark" : "light")}: {textKey} on {backgroundKey} is {ratio:0.00}:1");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Contrast_FollowsTheWcagFormula()
    {
        Assert.Equal(21, Contrast(Colors.Black, Colors.White), 2);
        Assert.Equal(1, Contrast(Colors.Red, Colors.Red), 2);
        Assert.Equal(4.54, Contrast(Color.Parse("#767676"), Colors.White), 2);
    }
}
