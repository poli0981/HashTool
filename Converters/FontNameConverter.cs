using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CheckHash.Converters;

public class FontNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FontFamily font)
        {
            if (font == FontFamily.Default ||
                (font.Name != null && (font.Name == FontFamily.Default.Name || font.Name == "$Default" || font.Name == "#Default")))
            {
                return "Default";
            }

            return font.Name;
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name)
        {
            if (name == "Default")
            {
                return FontFamily.Default;
            }
            return new FontFamily(name);
        }
        return value;
    }
}