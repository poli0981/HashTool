using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace CheckHash.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count && count == 0) return true;

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}