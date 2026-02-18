using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CheckHash.Models;
using CheckHash.Services;

namespace CheckHash.Converters;

public class FileStatusConverter : IValueConverter
{
    private static LocalizationService L => LocalizationService.Instance;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FileStatus status)
        {
            return status switch
            {
                FileStatus.Ready => L["Lbl_Status_Ready"],
                FileStatus.Processing => L["Status_Processing_Filter"],
                FileStatus.Success => L["Status_Done"],
                FileStatus.Failure => L["Status_Failure"],
                FileStatus.Cancelled => L["Status_Cancelled"],
                _ => status.ToString()
            };
        }

        if (value == null)
        {
            return L["Filter_All"];
        }

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}