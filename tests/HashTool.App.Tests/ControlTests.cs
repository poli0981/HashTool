using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HashTool.App.Services.Theme;
using HashTool.App.Views.Controls;
using HashTool.Core.Configuration;
using Xunit;

namespace HashTool.App.Tests;

public sealed class HighlightTextBlockControlTests
{
    private static Run[] Runs(HighlightTextBlock control) =>
        control.FindControl<TextBlock>("PART_TextBlock")!.Inlines!.OfType<Run>().ToArray();

    [AvaloniaFact]
    public void ShowsMatchesInBoldWithTheHighlightBrush()
    {
        var control = new HighlightTextBlock { HighlightBrush = Brushes.Orange, TextTrimming = TextTrimming.CharacterEllipsis };

        control.Text = "report.pdf";
        Assert.Equal(["report.pdf"], Runs(control).Select(r => r.Text));

        control.HighlightText = " REP ";
        var runs = Runs(control);
        Assert.Equal(["rep", "ort.pdf"], runs.Select(r => r.Text));
        Assert.Equal(FontWeight.Bold, runs[0].FontWeight);
        Assert.Same(Brushes.Orange, runs[0].Foreground);
        Assert.Equal(TextTrimming.CharacterEllipsis, control.TextTrimming);
        Assert.Equal(" REP ", control.HighlightText);
    }

    [AvaloniaFact]
    public void EmptyText_ShowsNothing()
    {
        var control = new HighlightTextBlock { HighlightText = "x", Text = "" };

        Assert.Empty(Runs(control));
        Assert.Equal("", control.Text);
    }

    [AvaloniaFact]
    public void ChangingTheBrush_RebuildsTheRuns()
    {
        var control = new HighlightTextBlock { Text = "aXa", HighlightText = "x" };

        control.HighlightBrush = Brushes.Red;

        Assert.Same(Brushes.Red, Runs(control).Single(r => r.Text == "X").Foreground);
        Assert.Same(Brushes.Red, control.HighlightBrush);
    }
}

public sealed class ThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryStyleDefinesEveryBrush(bool isDark)
    {
        foreach (var style in Enum.GetValues<AppThemeStyle>())
        {
            var palette = ThemePalettes.Get(style, isDark);
            Assert.All(ThemePalettes.Keys, key => Assert.IsAssignableFrom<IBrush>(palette[key]));
            Assert.Equal(ThemePalettes.Keys.Length, palette.Count);
        }
    }

    [AvaloniaFact]
    public void Apply_SetsTheVariantAndTheBrushesOfTheStyle()
    {
        var app = Application.Current!;
        var theme = new ThemeService();

        theme.CurrentThemeVariant = AppThemeVariant.Dark;
        foreach (var style in ThemeService.Styles)
        {
            theme.CurrentThemeStyle = style;
            Assert.True(theme.IsDark);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, app.RequestedThemeVariant);
            Assert.Equal(ThemePalettes.Get(style, isDark: true)["AppBackgroundBrush"].ToString(), app.Resources["AppBackgroundBrush"]!.ToString());
        }

        theme.CurrentThemeVariant = AppThemeVariant.Light;
        Assert.False(theme.IsDark);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.RequestedThemeVariant);

        theme.CurrentThemeVariant = AppThemeVariant.System;
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, app.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void Attach_FollowsThePlatformSetting()
    {
        var theme = new ThemeService { CurrentThemeStyle = AppThemeStyle.Retro, IsThemeLocked = true };

        theme.Attach(Application.Current!);

        Assert.Equal(Application.Current!.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark,
            theme.IsDark);
        Assert.True(theme.IsThemeLocked);
    }

    [AvaloniaFact]
    public void WindowEffect_TogglesTransparencyOnAWindow()
    {
        var window = new Window();
        window.Show();
        try
        {
            // The headless platform has no transparency, so Avalonia reports the fallback background: check the hints only.
            WindowEffect.Apply(window);
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                Assert.Contains(WindowTransparencyLevel.AcrylicBlur, window.TransparencyLevelHint);
            else
                Assert.Equal([WindowTransparencyLevel.None], window.TransparencyLevelHint);

            WindowEffect.Disable(window);
            Assert.Equal([WindowTransparencyLevel.None], window.TransparencyLevelHint);

            // Tests have no main window: these must simply do nothing.
            WindowEffect.ApplyToMainWindow();
            WindowEffect.DisableForMainWindow();
        }
        finally
        {
            window.Close();
        }
    }
}
