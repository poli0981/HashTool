using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CheckHash.Converters;

public class ComputeButtonEnabledConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count != 3)
            return false;

        if (values[0] is not bool isCancelled ||
            values[1] is not bool isProcessing ||
            values[2] is not bool isGlobalBusy)
        {
            return false;
        }

        return isProcessing || !isGlobalBusy;
    }
}