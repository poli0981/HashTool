using System;
using System.Text;

namespace HashTool.Core.Hashing;

/// <summary>Character encodings text can be hashed in. None of them writes a byte order mark.</summary>
public enum TextEncodingKind
{
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian
}

/// <summary>How line breaks are written into the bytes that are hashed.</summary>
public enum LineBreak
{
    /// <summary>LF, as on Linux and macOS (and in <c>printf</c> output).</summary>
    Lf,

    /// <summary>CR LF, as in Windows text files.</summary>
    CrLf
}

/// <summary>
/// Turns text into the exact bytes that get hashed: every line break (CR LF, CR or LF) written as the chosen one, the
/// chosen encoding without a byte order mark, and nothing added at the end. The same text therefore hashes the same on
/// every platform, whatever the text box inserts for Enter.
/// </summary>
public static class TextHashing
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UnicodeEncoding Utf16LittleEndian = new(bigEndian: false, byteOrderMark: false);
    private static readonly UnicodeEncoding Utf16BigEndian = new(bigEndian: true, byteOrderMark: false);

    public static byte[] GetBytes(string text, TextEncodingKind encoding, LineBreak lineBreak)
    {
        ArgumentNullException.ThrowIfNull(text);
        return GetEncoding(encoding).GetBytes(NormalizeLineBreaks(text, lineBreak));
    }

    public static Encoding GetEncoding(TextEncodingKind encoding) => encoding switch
    {
        TextEncodingKind.Utf8 => Utf8,
        TextEncodingKind.Utf16LittleEndian => Utf16LittleEndian,
        TextEncodingKind.Utf16BigEndian => Utf16BigEndian,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown text encoding.")
    };

    /// <summary>Display name of an encoding, the same in every language (like the algorithm names).</summary>
    public static string DisplayName(TextEncodingKind encoding) => encoding switch
    {
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf16LittleEndian => "UTF-16 LE",
        TextEncodingKind.Utf16BigEndian => "UTF-16 BE",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown text encoding.")
    };

    /// <summary>
    /// Writes CR LF, a lone CR and a lone LF as <paramref name="lineBreak"/>. Unlike <see cref="string.ReplaceLineEndings(string)"/>,
    /// other characters Unicode counts as line separators (form feed, NEL, U+2028, U+2029) are kept as they are.
    /// </summary>
    public static string NormalizeLineBreaks(string text, LineBreak lineBreak)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0) return text;

        var newLine = lineBreak == LineBreak.CrLf ? "\r\n" : "\n";
        var builder = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                builder.Append(newLine);
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n')
            {
                builder.Append(newLine);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
