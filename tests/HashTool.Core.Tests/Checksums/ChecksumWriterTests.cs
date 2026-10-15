using System;
using System.Linq;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests.Checksums;

public sealed class ChecksumWriterTests
{
    private static readonly ChecksumLine[] Lines =
    [
        new("a.txt", HashType.SHA256, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", 0),
        new("dir/b c.bin", HashType.MD5, "900150983CD24FB0D6963F7D28E17F72", 3)
    ];

    [Theory]
    [InlineData(ExportFormat.Gnu)]
    [InlineData(ExportFormat.Bsd)]
    public void RoundTripsThroughTheParser(ExportFormat format)
    {
        var text = ChecksumWriter.Write(Lines, format);
        var doc = ChecksumParser.Parse(text);

        Assert.Empty(doc.InvalidLines);
        Assert.Equal(Lines.Select(l => (l.Path, l.Hash.ToLowerInvariant())), doc.Entries.Select(e => (e.FileName!, e.Hash)));
        if (format == ExportFormat.Bsd) Assert.Equal(Lines.Select(l => l.Algorithm), doc.Entries.Select(e => e.Algorithm!.Value));
    }

    [Fact]
    public void Gnu_MatchesCoreutilsLayout()
    {
        var text = ChecksumWriter.Write(Lines.Take(1), ExportFormat.Gnu);

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  a.txt\n", text);
    }

    [Fact]
    public void Gnu_EscapesBackslashes()
    {
        var text = ChecksumWriter.Write([new ChecksumLine("odd\\name", HashType.CRC32, "CBF43926")], ExportFormat.Gnu, HexCase.Upper);

        Assert.Equal("\\CBF43926  odd\\\\name\n", text);
        Assert.Equal("odd\\name", ChecksumParser.Parse(text).Entries.Single().FileName);
    }

    [Fact]
    public void Sfv_RequiresCrc32()
    {
        Assert.Throws<ArgumentException>(() => ChecksumWriter.Write(Lines, ExportFormat.Sfv));

        var text = ChecksumWriter.Write([new ChecksumLine("file.iso", HashType.CRC32, "cbf43926")], ExportFormat.Sfv, HexCase.Upper);
        var doc = ChecksumParser.Parse(text, HashType.CRC32);
        Assert.Equal(("file.iso", "CBF43926"), (doc.Entries.Single().FileName!, doc.Entries.Single().Hash));
    }

    [Fact]
    public void Json_And_Csv()
    {
        var json = ChecksumWriter.Write(Lines, ExportFormat.Json);
        Assert.Contains("\"algorithm\": \"SHA256\"", json);
        Assert.Contains("\"path\": \"dir/b c.bin\"", json);

        var csv = ChecksumWriter.Write([new ChecksumLine("a,b.txt", HashType.MD5, "900150983CD24FB0D6963F7D28E17F72", 3)], ExportFormat.Csv);
        Assert.Equal("path,size,algorithm,hash\n\"a,b.txt\",3,MD5,900150983cd24fb0d6963f7d28e17f72\n", csv);
    }

    [Fact]
    public void Csv_NeutralisesFormulas()
    {
        var csv = ChecksumWriter.Write([new ChecksumLine("=HYPERLINK(1)", HashType.CRC32, "cbf43926")], ExportFormat.Csv);

        Assert.Contains("\n'=HYPERLINK(1),", csv);
    }

    [Fact]
    public void SuggestedExtension()
    {
        Assert.Equal("sha256", ChecksumWriter.SuggestedExtension(ExportFormat.Gnu, HashType.SHA256));
        Assert.Equal("txt", ChecksumWriter.SuggestedExtension(ExportFormat.Bsd, null));
        Assert.Equal("sfv", ChecksumWriter.SuggestedExtension(ExportFormat.Sfv, HashType.CRC32));
    }
}
