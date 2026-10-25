using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Checksums;
using HashTool.Core.Configuration;
using HashTool.Core.Files;
using HashTool.Core.Hashing;
using Xunit;

namespace HashTool.Core.Tests;

public sealed class HashAlgorithmNameTests
{
    public static TheoryData<HashType> All => new(HashAlgorithms.All);

    [Theory]
    [MemberData(nameof(All))]
    public void EveryAlgorithm_HasAnExtensionATagAndASize(HashType type)
    {
        Assert.True(HashAlgorithms.IsDefined(type));
        Assert.True(HashAlgorithms.TryParse(HashAlgorithms.FileExtension(type), out var fromExtension));
        Assert.Equal(type, fromExtension);
        Assert.True(HashAlgorithms.TryParse(HashAlgorithms.BsdTag(type), out var fromTag));
        Assert.Equal(type, fromTag);
        Assert.Equal(HashAlgorithms.SizeInBytes(type) * 2, HashAlgorithms.HexLength(type));
        Assert.Equal(type is HashType.MD5 or HashType.SHA1, type.IsInsecure());
    }

    [Fact]
    public void UnknownAlgorithms_AreRejected()
    {
        var unknown = (HashType)99;

        Assert.False(HashAlgorithms.IsDefined(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => HashAlgorithms.SizeInBytes(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => HashAlgorithms.FileExtension(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => HashAlgorithms.BsdTag(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => HashFunction.Create(unknown, 0));
    }

    [Theory]
    [InlineData(".sha256", HashType.SHA256)]
    [InlineData(" SHA-512 ", HashType.SHA512)]
    [InlineData("b3", HashType.BLAKE3)]
    [InlineData("XxHash3", HashType.XxHash3)]
    [InlineData("crc32", HashType.CRC32)]
    public void Names_AreParsedLeniently(string name, HashType expected)
    {
        Assert.True(HashAlgorithms.TryParse(name, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("sha3")]
    [InlineData("2")]
    public void UnknownNames_AreNotParsed(string? name)
    {
        Assert.False(HashAlgorithms.TryParse(name, out _));
    }

    [Fact]
    public async Task Results_PrintInUpperCaseByDefault()
    {
        var result = (await new HashEngine().ComputeStreamAsync(new MemoryStream("123456789"u8.ToArray()), [HashType.CRC32],
            cancellationToken: TestContext.Current.CancellationToken)).Single();

        Assert.Equal("CBF43926", result.ToString());
        Assert.Equal("cbf43926", result.ToHex(HexCase.Lower));
        Assert.Same(HashEngineOptions.Default, new HashEngine().Options);
    }
}

public sealed class FileErrorTests
{
    [Fact]
    public void Errors_AreClassifiedIndependentlyOfTheirMessages()
    {
        Assert.Equal(FileErrorKind.None, FileErrors.Classify(null));
        Assert.Equal(FileErrorKind.NotFound, FileErrors.Classify(new FileNotFoundException()));
        Assert.Equal(FileErrorKind.NotFound, FileErrors.Classify(new DirectoryNotFoundException()));
        Assert.Equal(FileErrorKind.AccessDenied, FileErrors.Classify(new UnauthorizedAccessException()));
        Assert.Equal(FileErrorKind.Io, FileErrors.Classify(new IOException("disk")));
        Assert.Equal(FileErrorKind.Unknown, FileErrors.Classify(new InvalidOperationException()));
        // ERROR_SHARING_VIOLATION is a Windows notion; elsewhere it is an ordinary I/O error.
        var sharing = new IOException("in use", unchecked((int)0x80070020));
        Assert.Equal(OperatingSystem.IsWindows() ? FileErrorKind.InUse : FileErrorKind.Io, FileErrors.Classify(sharing));
    }
}

public sealed class SettingLimitTests
{
    [Theory]
    [InlineData(FileSizeUnit.Byte, 1L)]
    [InlineData(FileSizeUnit.KB, ByteSize.OneKB)]
    [InlineData(FileSizeUnit.MB, ByteSize.OneMB)]
    [InlineData(FileSizeUnit.GB, ByteSize.OneGB)]
    [InlineData(FileSizeUnit.TB, ByteSize.OneTB)]
    [InlineData(FileSizeUnit.PB, ByteSize.OnePB)]
    public void SizeLimit_IsTheValueTimesTheUnit(FileSizeUnit unit, long multiplier)
    {
        var settings = new AppSettings { IsFileSizeLimitEnabled = true, FileSizeLimitValue = 3, FileSizeLimitUnit = unit };

        Assert.Equal(3 * multiplier, settings.MaxFileSizeBytes());
    }

    [Fact]
    public void SizeLimit_OffOrHuge_IsUnlimited()
    {
        Assert.Equal(long.MaxValue, new AppSettings { IsFileSizeLimitEnabled = false }.MaxFileSizeBytes());
        Assert.Equal(long.MaxValue,
            new AppSettings { IsFileSizeLimitEnabled = true, FileSizeLimitValue = 1_000_000, FileSizeLimitUnit = FileSizeUnit.PB }.MaxFileSizeBytes());
        Assert.EndsWith("settings", SettingsStore.DefaultSettingsDirectory, StringComparison.Ordinal);
    }
}

public sealed class ChecksumEdgeTests
{
    [Theory]
    [InlineData(ExportFormat.Sfv, null, "sfv")]
    [InlineData(ExportFormat.Json, null, "json")]
    [InlineData(ExportFormat.Csv, null, "csv")]
    [InlineData(ExportFormat.Gnu, HashType.MD5, "md5")]
    [InlineData(ExportFormat.Bsd, null, "txt")]
    public void SuggestedExtensions(ExportFormat format, HashType? single, string expected)
    {
        Assert.Equal(expected, ChecksumWriter.SuggestedExtension(format, single));
    }

    [Fact]
    public void Writing_RejectsWhatTheFormatCannotHold()
    {
        ChecksumLine[] lines = [new("a.txt", HashType.SHA256, "00")];

        Assert.Throws<ArgumentException>(() => ChecksumWriter.Write(lines, ExportFormat.Sfv));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChecksumWriter.Write(lines, (ExportFormat)42));
    }

    [Fact]
    public void GnuNamesWithBackslashesOrLineBreaks_AreEscapedAndReadBack()
    {
        var name = "dir\\odd\nname\r.txt";
        var text = ChecksumWriter.Write([new ChecksumLine(name, HashType.MD5, "5D41402ABC4B2A76B9719D911017C592")], ExportFormat.Gnu);

        Assert.Equal("\\5d41402abc4b2a76b9719d911017c592  dir\\\\odd\\nname\\r.txt\n", text);
        Assert.Equal(name, Assert.Single(ChecksumParser.Parse(text).Entries).FileName);
        Assert.Equal("bxname", Assert.Single(ChecksumParser.Parse("\\5d41402abc4b2a76b9719d911017c592  b\\xname").Entries).FileName);
    }

    [Fact]
    public void OverlongLines_AreInvalid()
    {
        var document = ChecksumParser.Parse($"{new string('a', 64)}  {new string('n', 40_000)}\n");

        Assert.Empty(document.Entries);
        Assert.Equal([1], document.InvalidLines);
    }

    [Theory]
    [InlineData(@"\\server\share\file.iso")]
    [InlineData("//server/share/file.iso")]
    [InlineData("C:/data/file\0.iso")]
    public void UnsafePaths_AreRejected(string path)
    {
        Assert.False(ChecksumEntry.IsSafePath(path));
    }

    [Fact]
    public void DevicePaths_AreRejectedOutsideWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), ChecksumEntry.IsSafePath("/dev/sda"));
        Assert.Equal(OperatingSystem.IsWindows(), ChecksumEntry.IsSafePath("/proc/self/environ"));
        Assert.Null(new ChecksumEntry("00", "bad\0name", HashType.MD5, false, 1).ResolvePath(Path.GetTempPath()));
    }
}

public sealed class FileEnumeratorEdgeTests
{
    [Fact]
    public void Expand_ReportsMissingAndInvalidPaths_AndSkipsBlanks()
    {
        using var temp = new TempDirectory();
        var file = temp.File("a.txt", "x");
        var errors = new List<string>();

        var found = FileEnumerator.Expand([file, "  ", Path.Combine(temp.Path, "missing.txt"), "bad\0path"], null,
            (path, _) => errors.Add(path), TestContext.Current.CancellationToken).ToList();

        Assert.Equal(file, Assert.Single(found).FullPath);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void EnumeratingAMissingFolder_ReportsIt()
    {
        var errors = new List<string>();
        var missing = Path.Combine(Path.GetTempPath(), $"hashtool-missing-{Guid.NewGuid():N}");

        Assert.Empty(FileEnumerator.EnumerateDirectory(missing, null, (path, _) => errors.Add(path), TestContext.Current.CancellationToken));
        Assert.Equal([missing], errors);
    }

    [Fact]
    public void Expand_StopsWhenCancelled()
    {
        using var temp = new TempDirectory();
        temp.File("a.txt", "x");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileEnumerator.Expand([temp.Path], null, null, cancelled.Token).ToList());
        Assert.Throws<OperationCanceledException>(() => FileEnumerator.EnumerateDirectory(temp.Path, null, null, cancelled.Token).ToList());
    }
}
