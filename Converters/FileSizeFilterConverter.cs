using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CheckHash.Models;
using CheckHash.Services;

namespace CheckHash.Converters;

public class FileSizeFilterConverter : IValueConverter
{
    private LocalizationService L => LocalizationService.Instance;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FileSizeFilter filter)
        {
            return filter switch
            {
                FileSizeFilter.All => L["Size_All"],
                FileSizeFilter.Small => L["Size_Small"],
                FileSizeFilter.Medium => L["Size_Medium"],
                FileSizeFilter.Large => L["Size_Large"],
                FileSizeFilter.ExtraLarge => L["Size_ExtraLarge"],
                _ => filter.ToString()
            };
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}