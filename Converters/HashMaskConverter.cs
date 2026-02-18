using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CheckHash.Converters;

public class HashMaskConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 3) return "";

        var hash = values[0] as string ?? "";
        var isMaskingEnabled = values[1] as bool? ?? false;
        var isRevealed = values[2] as bool? ?? false;

        if (parameter as string == "MaskOnly")
        {
            return MaskHash(hash);
        }

        if (string.IsNullOrEmpty(hash)) return "";

        if (!isMaskingEnabled) return hash;

        if (isRevealed) return hash;

        return MaskHash(hash);
    }

    private static string MaskHash(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return "";

        int length = hash.Length;
        if (length <= 8) return new string('*', length);

        return string.Create(length, hash, (span, h) =>
        {
            h.AsSpan(0, 4).CopyTo(span);
            span[4..^4].Fill('*');
            h.AsSpan(h.Length - 4).CopyTo(span[^4..]);
        });
    }
}