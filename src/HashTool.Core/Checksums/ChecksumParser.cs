using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Hashing;

namespace HashTool.Core.Checksums;

/// <summary>
/// Reads checksum files written by Hash Tool, GNU coreutils (<c>sha256sum</c>), BSD/macOS (<c>shasum --tag</c>,
/// <c>md5</c>), xxhsum, b3sum, SFV tools and Windows <c>certutil</c>.
/// </summary>
public static partial class ChecksumParser
{
    /// <summary>Larger files are rejected instead of being read into memory.</summary>
    public const long MaxFileSize = 16L * ByteSize.OneMB;

    [GeneratedRegex(@"^(?<tag>[A-Za-z][A-Za-z0-9_\-]*)\s?\((?<name>.*)\)\s?=\s?(?<hash>[0-9A-Fa-f]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BsdLine();

    [GeneratedRegex(@"^(?<escaped>\\)?(?<xxh3>XXH3_)?(?<hash>[0-9A-Fa-f]{8,128})(?: \*| {1,2})(?<name>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GnuLine();

    [GeneratedRegex(@"^(?<name>.+?)\s+(?<hash>[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex SfvLine();

    [GeneratedRegex(@"^(?:XXH3_)?[0-9A-Fa-f]{2}(?:\s?[0-9A-Fa-f]{2}){3,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex BareLine();

    [GeneratedRegex(@"^(?:[A-Za-z0-9\-]+ hash of .*:|CertUtil: .*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CertUtilNoise();

    /// <summary>Reads and parses a checksum file.</summary>
    /// <param name="path">The checksum file.</param>
    /// <param name="userHint">Algorithm to prefer when the file itself does not say (e.g. the one selected in the UI).</param>
    public static async Task<ChecksumDocument> ParseFileAsync(string path, HashType? userHint = null,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileSize)
            throw new InvalidDataException($"The checksum file is larger than {MaxFileSize / ByteSize.OneMB} MB.");

        string text;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        ChecksumFileNames.IsChecksumFile(path, out var nameHint);
        return Parse(text, nameHint, userHint);
    }

    /// <summary>Parses checksum file content.</summary>
    /// <param name="text">File content.</param>
    /// <param name="fileNameHint">Algorithm implied by the checksum file's name (<c>.sha256</c>, <c>SHA256SUMS</c>).</param>
    /// <param name="userHint">Algorithm to prefer when neither the line nor the file name decides.</param>
    public static ChecksumDocument Parse(string text, HashType? fileNameHint = null, HashType? userHint = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<ChecksumEntry>();
        var invalid = new List<int>();
        ChecksumFormat? format = null;
        var lineNumber = 0;

        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r').ToString();
            if (lineNumber == 1) line = line.TrimStart('﻿');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';' || CertUtilNoise().IsMatch(trimmed)) continue;

            if (TryParseLine(line, trimmed, fileNameHint, userHint, lineNumber, out var entry, out var lineFormat))
            {
                entries.Add(entry);
                format ??= lineFormat;
            }
            else
            {
                invalid.Add(lineNumber);
            }
        }

        return new ChecksumDocument(entries, invalid, format ?? ChecksumFormat.Gnu);
    }

    private static bool TryParseLine(string line, string trimmed, HashType? fileNameHint, HashType? userHint, int lineNumber,
        out ChecksumEntry entry, out ChecksumFormat format)
    {
        entry = null!;

        var bsd = BsdLine().Match(trimmed);
        if (bsd.Success && HashAlgorithms.TryParse(bsd.Groups["tag"].Value, out var tagged))
        {
            format = ChecksumFormat.Bsd;
            return TryCreate(bsd.Groups["hash"].Value, bsd.Groups["name"].Value, tagged, fileNameHint, userHint, lineNumber, out entry);
        }

        var gnu = GnuLine().Match(line.TrimStart());
        if (gnu.Success)
        {
            format = ChecksumFormat.Gnu;
            var name = gnu.Groups["name"].Value;
            if (gnu.Groups["escaped"].Success) name = UnescapeGnu(name);
            HashType? explicitType = gnu.Groups["xxh3"].Success ? HashType.XxHash3 : null;
            if (TryCreate(gnu.Groups["hash"].Value, name, explicitType, fileNameHint, userHint, lineNumber, out entry)) return true;
        }

        var bare = BareLine().Match(trimmed);
        if (bare.Success)
        {
            format = ChecksumFormat.Bare;
            var hash = HashMatcher.Normalize(trimmed);
            HashType? explicitType = trimmed.StartsWith("XXH3_", StringComparison.OrdinalIgnoreCase) ? HashType.XxHash3 : null;
            return TryCreate(hash, null, explicitType, fileNameHint, userHint, lineNumber, out entry);
        }

        var sfv = SfvLine().Match(trimmed);
        if (sfv.Success)
        {
            format = ChecksumFormat.Sfv;
            return TryCreate(sfv.Groups["hash"].Value, sfv.Groups["name"].Value, HashType.CRC32, fileNameHint, userHint,
                lineNumber, out entry);
        }

        format = default;
        return false;
    }

    private static bool TryCreate(string hash, string? name, HashType? explicitType, HashType? fileNameHint, HashType? userHint,
        int lineNumber, out ChecksumEntry entry)
    {
        entry = null!;
        hash = HashMatcher.Normalize(hash);
        if (!HashMatcher.IsHex(hash)) return false;

        HashType? algorithm = null;
        var guessed = false;
        if (explicitType is { } e && HashAlgorithms.HexLength(e) == hash.Length) algorithm = e;
        else if (explicitType is not null) return false;
        else if (fileNameHint is { } f && HashAlgorithms.HexLength(f) == hash.Length) algorithm = f;
        else if (userHint is { } u && HashAlgorithms.HexLength(u) == hash.Length) algorithm = u;
        else
        {
            var candidates = HashAlgorithms.CandidatesForHexLength(hash.Length);
            if (candidates.Count == 0) return false;
            algorithm = candidates[0];
            guessed = candidates.Count > 1;
        }

        entry = new ChecksumEntry(hash, string.IsNullOrEmpty(name) ? null : name, algorithm, guessed, lineNumber);
        return true;
    }

    /// <summary>coreutils escapes <c>\</c> and newlines in names and marks such lines with a leading backslash.</summary>
    private static string UnescapeGnu(string name)
    {
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 1 < name.Length)
            {
                var next = name[++i];
                builder.Append(next switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    _ => next
                });
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }
}
