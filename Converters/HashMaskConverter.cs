using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Data.Converters;

namespace CheckHash.Converters;

public class HashMaskConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0]: Hash string to be masked
        // values[1]: IsHashMaskingEnabled (Global Setting)
        // values[2]: IsRevealed (Local Item State)

        if (values.Count < 3) return "";

        var hash = values[0] as string ?? "";
        var isMaskingEnabled = values[1] as bool? ?? false;
        var isRevealed = values[2] as bool? ?? false;

        // If parameter is "MaskOnly"  -> Hide hash
        // Logic use for CheckHashView when user choose "Show Only Masked" option
        if (parameter as string == "MaskOnly")
        {
             if (string.IsNullOrEmpty(hash)) return "";
             if (hash.Length <= 8) return new string('*', hash.Length);
             return $"{hash[..4]}{new string('*', hash.Length - 8)}{hash[^4..]}";
        }

        if (string.IsNullOrEmpty(hash)) return "";

        // If masking is disabled -> Show full hash
        if (!isMaskingEnabled) return hash;

        // If revealed -> Show full hash
        if (isRevealed) return hash;

        // Otherwise, show masked hash of format: "abcd****wxyz"
        if (hash.Length <= 8) return new string('*', hash.Length);
        
        return $"{hash[..4]}{new string('*', hash.Length - 8)}{hash[^4..]}";
    }
}