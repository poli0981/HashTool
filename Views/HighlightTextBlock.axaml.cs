using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;

namespace CheckHash.Views.Controls;

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

        int index = 0;
        while (index < text.Length)
        {
            int matchIndex = text.IndexOf(highlight, index, StringComparison.OrdinalIgnoreCase);
            if (matchIndex == -1)
            {
                if (index < text.Length)
                {
                    _textBlock.Inlines?.Add(new Run { Text = text.Substring(index) });
                }
                break;
            }

            if (matchIndex > index)
            {
                _textBlock.Inlines?.Add(new Run { Text = text.Substring(index, matchIndex - index) });
            }

            var matchText = text.Substring(matchIndex, highlight.Length);
            _textBlock.Inlines?.Add(new Run
            {
                Text = matchText,
                Foreground = HighlightBrush,
                FontWeight = FontWeight.Bold
            });

            index = matchIndex + highlight.Length;
        }
        
        if (index < text.Length)
        {
             _textBlock.Inlines?.Add(new Run { Text = text.Substring(index) });
        }
    }
}