using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace HashTool.App.Views.Controls;

public partial class HighlightTextBlock : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string?> HighlightTextProperty =
        AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(HighlightText));

    public string? HighlightText
    {
        get => GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<HighlightTextBlock, IBrush?>(nameof(HighlightBrush));

    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public static readonly StyledProperty<TextTrimming> TextTrimmingProperty =
        AvaloniaProperty.Register<HighlightTextBlock, TextTrimming>(nameof(TextTrimming));

    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    private TextBlock? _textBlock;

    public HighlightTextBlock()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _textBlock = this.FindControl<TextBlock>("PART_TextBlock");
        UpdateText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty ||
            change.Property == HighlightTextProperty ||
            change.Property == HighlightBrushProperty)
        {
            UpdateText();
        }
    }

    private void UpdateText()
    {
        if (_textBlock == null) return;

        _textBlock.Inlines?.Clear();

        var text = Text;
        var highlight = HighlightText;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (string.IsNullOrEmpty(highlight))
        {
            _textBlock.Inlines?.Add(new Run { Text = text });
            return;
        }

        foreach (var (segment, isMatch) in Split(text, highlight.Trim()))
        {
            _textBlock.Inlines?.Add(isMatch
                ? new Run { Text = segment, Foreground = HighlightBrush, FontWeight = FontWeight.Bold }
                : new Run { Text = segment });
        }
    }

    /// <summary>Splits <paramref name="text"/> into alternating plain and matching segments (case-insensitive).</summary>
    internal static System.Collections.Generic.IEnumerable<(string Segment, bool IsMatch)> Split(string text, string highlight)
    {
        if (highlight.Length == 0)
        {
            yield return (text, false);
            yield break;
        }

        // Same comparison as the list filter; a culture-aware match can be longer or shorter than the search text.
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        var index = 0;
        while (index < text.Length)
        {
            var offset = compare.IndexOf(text.AsSpan(index), highlight, System.Globalization.CompareOptions.IgnoreCase, out var length);
            if (offset < 0 || length == 0) break;
            var match = index + offset;
            if (match > index) yield return (text[index..match], false);
            yield return (text.Substring(match, length), true);
            index = match + length;
        }

        // The tail after the last match is emitted exactly once (1.x appended it twice).
        if (index < text.Length) yield return (text[index..], false);
    }
}
