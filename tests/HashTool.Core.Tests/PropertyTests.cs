using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests;

/// <summary>
/// Property-based tests (FsCheck): every property runs on hundreds of random inputs, including text nobody would write by
/// hand, and a failure reports the smallest input that breaks it.
/// </summary>
public sealed class PropertyTests
{
    private static readonly Config Runs = Config.QuickThrowOnFailure.WithMaxTest(500);

    private static readonly char[] Noise = "abcXYZ019 _-.,;:*=()[]{}#'\"/\\\t\r\néßЖ漢".ToCharArray();
    private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

    private static Gen<string> StringOf(char[] chars, int maxLength) =>
        Gen.Choose(0, maxLength).SelectMany(n => Gen.ArrayOf(Gen.Elements(chars), n)).Select(a => new string(a));

    private static Gen<string> Hex(int length) => Gen.ArrayOf(Gen.Elements(HexDigits), length).Select(a => new string(a));

    /// <summary>A name with no leading or trailing space that does not start with ';' (a comment in SFV files).</summary>
    private static Gen<string> FileName(string extra)
    {
        var inner = ("abcXYZ019 _-.()[]#=*é漢" + extra).ToCharArray();
        var edge = "abcXYZ019_-.()[]é漢".ToCharArray();
        return Gen.Elements(edge).SelectMany(first => StringOf(inner, 20), (first, middle) => (first, middle))
            .SelectMany(fm => Gen.Elements(edge), (fm, last) => fm.middle.Length == 0 ? fm.first.ToString() : fm.first + fm.middle + last);
    }

    private static Gen<(string Name, string Hash)[]> Lines(HashType algorithm, string extraNameChars) =>
        Gen.Choose(1, 6).SelectMany(n => Gen.ArrayOf(FileName(extraNameChars).Zip(Hex(HashAlgorithms.HexLength(algorithm))), n));

    private static void AssertSameEntries(ChecksumDocument document, (string Name, string Hash)[] lines, HashType algorithm)
    {
        Assert.Empty(document.InvalidLines);
        Assert.Equal(lines.Length, document.Entries.Count);
        for (var i = 0; i < lines.Length; i++)
        {
            Assert.Equal(lines[i].Name, document.Entries[i].FileName);
            Assert.Equal(lines[i].Hash, document.Entries[i].Hash, ignoreCase: true);
            Assert.Equal(algorithm, document.Entries[i].Algorithm);
        }
    }

    [Fact]
    public void Parser_AcceptsAnyText()
    {
        // Random text, and random lines shaped like checksum lines, never make the parser throw; whatever it accepts is a
        // hexadecimal digest of the algorithm's length.
        var lineLike = Gen.OneOf(
            StringOf(Noise, 60),
            Gen.Elements(HashAlgorithms.All.ToArray()).SelectMany(a => Hex(HashAlgorithms.HexLength(a)))
                .SelectMany(h => StringOf(Noise, 20), (h, name) => h + "  " + name),
            Gen.Elements(HashAlgorithms.All.ToArray()).SelectMany(a => Hex(HashAlgorithms.HexLength(a)), (a, h) => (a, h))
                .SelectMany(x => StringOf(Noise, 20), (x, name) => $"{HashAlgorithms.BsdTag(x.a)} ({name}) = {x.h}"));
        var text = Gen.ListOf(lineLike).Select(l => string.Join("\n", l));

        Check.One(Runs, Prop.ForAll(text.ToArbitrary(), input =>
        {
            foreach (var preferSfv in new[] { false, true })
            {
                var document = ChecksumParser.Parse(input, preferSfv: preferSfv);
                Assert.All(document.Entries, e =>
                {
                    Assert.True(HashMatcher.IsHex(e.Hash));
                    if (e.Algorithm is { } algorithm) Assert.Equal(HashAlgorithms.HexLength(algorithm), e.Hash.Length);
                });
            }
        }));
    }

    [Fact]
    public void GnuFiles_ReadBackWhatWasWritten_IncludingBackslashesAndLineBreaksInNames()
    {
        var input = Gen.Elements(HashAlgorithms.All.ToArray()).SelectMany(a => Lines(a, "\\\n"), (a, lines) => (a, lines));

        Check.One(Runs, Prop.ForAll(input.ToArbitrary(), x =>
        {
            var text = ChecksumWriter.Write(x.lines.Select(l => new ChecksumLine(l.Name, x.a, l.Hash)), ExportFormat.Gnu);
            AssertSameEntries(ChecksumParser.Parse(text, fileNameHint: x.a), x.lines, x.a);
        }));
    }

    [Fact]
    public void BsdFiles_ReadBackWhatWasWritten()
    {
        var input = Gen.Elements(HashAlgorithms.All.ToArray()).SelectMany(a => Lines(a, ""), (a, lines) => (a, lines));

        Check.One(Runs, Prop.ForAll(input.ToArbitrary(), x =>
        {
            var text = ChecksumWriter.Write(x.lines.Select(l => new ChecksumLine(l.Name, x.a, l.Hash)), ExportFormat.Bsd);
            AssertSameEntries(ChecksumParser.Parse(text), x.lines, x.a);
        }));
    }

    [Fact]
    public void SfvFiles_ReadBackWhatWasWritten()
    {
        Check.One(Runs, Prop.ForAll(Lines(HashType.CRC32, "").ToArbitrary(), lines =>
        {
            var text = ChecksumWriter.Write(lines.Select(l => new ChecksumLine(l.Name, HashType.CRC32, l.Hash)), ExportFormat.Sfv,
                HexCase.Upper);
            AssertSameEntries(ChecksumParser.Parse(text, preferSfv: true), lines, HashType.CRC32);
        }));
    }

    [Fact]
    public void ResolvePath_NeverReturnsANetworkOrDevicePath()
    {
        var prefixes = new[] { "", "//", "\\\\", "\\\\?\\", "/dev/", "/proc/", "C:", "../", "./", "a/../../" };
        var name = Gen.Elements(prefixes).SelectMany(_ => StringOf(Noise.Append((char)0).ToArray(), 30), (prefix, rest) => prefix + rest);
        var baseDirectory = Path.GetTempPath();

        Check.One(Runs, Prop.ForAll(name.ToArbitrary(), fileName =>
        {
            var resolved = new ChecksumEntry("00000000", fileName, HashType.CRC32, false, 1).ResolvePath(baseDirectory);
            if (fileName.StartsWith("//", StringComparison.Ordinal) || fileName.StartsWith("\\\\", StringComparison.Ordinal) ||
                fileName.Contains((char)0, StringComparison.Ordinal))
                Assert.Null(resolved);
            if (resolved is not null)
            {
                Assert.True(ChecksumEntry.IsSafePath(resolved), resolved);
                Assert.True(Path.IsPathFullyQualified(resolved), resolved);
            }
        }));
    }

    [Fact]
    public void Matcher_IgnoresCaseSpacesAndPrefix()
    {
        var digest = Gen.ArrayOf(Gen.Choose(0, 255).Select(b => (byte)b), 32);

        Check.One(Runs, Prop.ForAll(digest.ToArbitrary(), bytes =>
        {
            var actual = new HashResult(HashType.SHA256, bytes);
            var hex = actual.ToHex(HexCase.Lower);
            foreach (var expected in new[] { hex, hex.ToUpperInvariant(), "0x" + hex, $" {hex[..32]} {hex[32..]}\t" })
                Assert.Equal(HashMatch.Match, HashMatcher.Compare(expected, actual));
            Assert.Equal(HashMatch.LengthMismatch, HashMatcher.Compare(hex[..^2], actual));
            Assert.Equal(HashMatch.InvalidExpected, HashMatcher.Compare("g" + hex[1..], actual));
        }));
    }

    [Fact]
    public void Matcher_AcceptsTheReversedCrc32OfHashTool11()
    {
        var value = Gen.Choose(0, int.MaxValue).Zip(Gen.Choose(0, 1), (low, high) => (uint)low | ((uint)high << 31));

        Check.One(Runs, Prop.ForAll(value.ToArbitrary(), crc =>
        {
            var digest = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(digest, crc);
            var reversed = BinaryPrimitives.ReverseEndianness(crc).ToString("x8", CultureInfo.InvariantCulture);
            var expected = BinaryPrimitives.ReverseEndianness(crc) == crc ? HashMatch.Match : HashMatch.LegacyByteOrderMatch;
            Assert.Equal(expected, HashMatcher.Compare(reversed, new HashResult(HashType.CRC32, digest)));
        }));
    }

    [Fact]
    public void LineBreaks_AreAllWrittenTheChosenWay()
    {
        var input = StringOf(Noise, 80).Zip(Gen.Elements(LineBreak.Lf, LineBreak.CrLf));

        Check.One(Runs, Prop.ForAll(input.ToArbitrary(), x =>
        {
            var (text, lineBreak) = x;
            var normalized = TextHashing.NormalizeLineBreaks(text, lineBreak);
            Assert.Equal(normalized, TextHashing.NormalizeLineBreaks(normalized, lineBreak));
            if (lineBreak == LineBreak.Lf) Assert.DoesNotContain('\r', normalized);
            else Assert.Equal(normalized.Count(c => c == '\r'), normalized.Split("\r\n").Length - 1);
            Assert.Equal(2 * normalized.Length, TextHashing.GetBytes(text, TextEncodingKind.Utf16LittleEndian, lineBreak).Length);
        }));
    }
}
