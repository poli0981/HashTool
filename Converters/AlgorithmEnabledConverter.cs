using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using CheckHash.Models;

namespace CheckHash.Converters;

public class AlgorithmEnabledConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count != 2)
            return false;

        if (values[0] is not FileStatus status ||
            values[1] is not bool isComputing)
        {
            return false;
        }

        if (!isComputing)
        {
            return true;
        }

        // If computing, disable for Ready (Queue) or Processing (Active)
        // Enable for Success, Failure, Cancelled (Complete)
        return status != FileStatus.Ready && status != FileStatus.Processing;
    }
}