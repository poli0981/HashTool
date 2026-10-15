using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HashTool.App.Services;

namespace HashTool.App.Converters;

/// <summary>Shows the default font as a translated "Default" label.</summary>
public sealed class FontNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FontFamily font when font == FontFamily.Default || font.Name.StartsWith('$') =>
            LocalizationService.Current?["Font_Default"] ?? "Default",
        FontFamily font => font.Name,
        _ => value
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
