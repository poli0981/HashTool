using System;
using System.Globalization;

namespace HashTool.Core;

/// <summary>Binary size constants and human-readable formatting.</summary>
public static class ByteSize
{
    public const int OneKB = 1024;
    public const int OneMB = OneKB * 1024;
    public const int OneGB = OneMB * 1024;
    public const long OneTB = (long)OneGB * 1024;
    public const long OnePB = OneTB * 1024;

    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a byte count such as <c>1.5 GB</c> using the given culture's decimal separator.</summary>
    public static string Format(long bytes, IFormatProvider? culture = null)
    {
        if (bytes < 0) bytes = 0;
        double value = bytes;
        var unit = 0;
        while (value >= OneKB && unit < Units.Length - 1)
        {
            value /= OneKB;
            unit++;
        }

        return string.Format(culture ?? CultureInfo.CurrentCulture, "{0:0.##} {1}", value, Units[unit]);
    }
}
