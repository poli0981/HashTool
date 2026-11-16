using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Hashing;

public sealed class TextHashingTests
{
    [Fact]
    public void Utf8_HasNoByteOrderMarkAndNothingAddedAtTheEnd()
    {
        Assert.Equal("abc"u8.ToArray(), TextHashing.GetBytes("abc", TextEncodingKind.Utf8, LineBreak.Lf));
        Assert.Equal("é€"u8.ToArray(), TextHashing.GetBytes("é€", TextEncodingKind.Utf8, LineBreak.Lf));
        Assert.Empty(TextHashing.GetBytes("", TextEncodingKind.Utf8, LineBreak.Lf));
    }

    [Fact]
    public void Utf16_IsWrittenInTheChosenByteOrderWithoutAByteOrderMark()
    {
        Assert.Equal(new byte[] { 0x61, 0, 0x62, 0, 0x63, 0 }, TextHashing.GetBytes("abc", TextEncodingKind.Utf16LittleEndian, LineBreak.Lf));
        Assert.Equal(new byte[] { 0, 0x61, 0, 0x62, 0, 0x63 }, TextHashing.GetBytes("abc", TextEncodingKind.Utf16BigEndian, LineBreak.Lf));
    }

    [Theory]
    [InlineData("a\r\nb\rc\nd", LineBreak.Lf, "a\nb\nc\nd")]
    [InlineData("a\r\nb\rc\nd", LineBreak.CrLf, "a\r\nb\r\nc\r\nd")]
    [InlineData("a\n\nb\r\n", LineBreak.CrLf, "a\r\n\r\nb\r\n")]
    [InlineData("a\r\r\nb", LineBreak.Lf, "a\n\nb")]
    [InlineData("no line breaks", LineBreak.CrLf, "no line breaks")]
    public void LineBreaks_AreWrittenAsChosen(string text, LineBreak lineBreak, string expected) =>
        Assert.Equal(expected, TextHashing.NormalizeLineBreaks(text, lineBreak));

    [Fact]
    public void OtherUnicodeSeparators_AreKept()
    {
        // Form feed, NEL and the Unicode line and paragraph separators are text, not line breaks of the text box.
        var text = string.Concat("a", char.ConvertFromUtf32(0x0C), "b", char.ConvertFromUtf32(0x85), "c", char.ConvertFromUtf32(0x2028), "d",
            char.ConvertFromUtf32(0x2029), "e");

        Assert.Equal(text, TextHashing.NormalizeLineBreaks(text, LineBreak.CrLf));
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf8, "UTF-8")]
    [InlineData(TextEncodingKind.Utf16LittleEndian, "UTF-16 LE")]
    [InlineData(TextEncodingKind.Utf16BigEndian, "UTF-16 BE")]
    public void Encodings_HaveTheirUsualNames(TextEncodingKind encoding, string name) =>
        Assert.Equal(name, TextHashing.DisplayName(encoding));

    [Fact]
    public void UnknownValues_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextHashing.GetEncoding((TextEncodingKind)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextHashing.DisplayName((TextEncodingKind)42));
        Assert.Throws<ArgumentNullException>(() => TextHashing.GetBytes(null!, TextEncodingKind.Utf8, LineBreak.Lf));
    }

    [Fact]
    public async Task TextHashes_MatchTheHashesOfAFileWithTheSameBytes()
    {
        using var temp = new TempDirectory();
        var engine = new HashEngine();
        var token = TestContext.Current.CancellationToken;
        const string text = "Hash Tool\r\nsecond line\nthird ✓";
        var bytes = TextHashing.GetBytes(text, TextEncodingKind.Utf8, LineBreak.Lf);
        var file = temp.File("text.txt", bytes);

        using var memory = new MemoryStream(bytes, writable: false);
        var fromText = await engine.ComputeStreamAsync(memory, HashAlgorithms.All, cancellationToken: token);
        var fromFile = await engine.ComputeFileAsync(file, HashAlgorithms.All, cancellationToken: token);

        Assert.Equal(fromFile.Select(r => r.ToHex()), fromText.Select(r => r.ToHex()));
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            (await engine.ComputeStreamAsync(new MemoryStream("abc"u8.ToArray()), [HashType.SHA256], cancellationToken: token))[0].ToHex());
    }
}
