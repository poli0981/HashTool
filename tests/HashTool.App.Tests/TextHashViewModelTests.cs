using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.Core;
using HashTool.Core.Hashing;
using HashTool.Core.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>A "Hash Text" page of its own, with its own settings and clipboard; it hashes without waiting.</summary>
internal sealed class TextPage : IDisposable
{
    public TextPage()
    {
        Settings = TestServices.Settings(System.IO.Path.Combine(Temp.Path, ".settings"));
        Vm = new TextHashViewModel(new HashEngine(), Settings, Clipboard, TestServices.Localization,
            TestServices.Loggers.CreateLogger<TextHashViewModel>())
        { Delay = TimeSpan.Zero };
    }

    public TempDirectory Temp { get; } = new();

    public SettingsService Settings { get; }

    public RecordingClipboard Clipboard { get; } = new();

    public TextHashViewModel Vm { get; }

    public string HashOf(HashType type) => Vm.Results.Single(r => r.Algorithm == type).Hash;

    public async Task TypeAsync(string text)
    {
        Vm.Text = text;
        await Vm.Pending;
    }

    public void Dispose()
    {
        Settings.Dispose();
        Temp.Dispose();
    }
}

public sealed class TextHashViewModelTests
{
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [AvaloniaFact]
    public async Task ListsEveryAlgorithm_StartingWithTheHashesOfNoText()
    {
        using var page = new TextPage();
        await page.Vm.Pending;

        Assert.Equal(HashAlgorithms.All, page.Vm.Results.Select(r => r.Algorithm));
        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", page.HashOf(HashType.SHA256));
        Assert.Equal(TestServices.Localization.Format("Lbl_TextSize", ByteSize.Format(0)), page.Vm.SizeText);
    }

    [AvaloniaFact]
    public async Task HashesTheTypedText_WithEveryAlgorithm()
    {
        using var page = new TextPage();

        await page.TypeAsync("abc");

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", page.HashOf(HashType.SHA256));
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", page.HashOf(HashType.MD5));
        Assert.Equal("6437B3AC38465133FFB63B75273A8DB548C558465D79DB03FD359C6CD5BD9D85", page.HashOf(HashType.BLAKE3));
        Assert.Equal("352441C2", page.HashOf(HashType.CRC32));
        Assert.All(page.Vm.Results, r => Assert.False(string.IsNullOrEmpty(r.Hash)));
        Assert.Equal(TestServices.Localization.Format("Lbl_TextSize", ByteSize.Format(3)), page.Vm.SizeText);
    }

    [AvaloniaFact]
    public async Task EncodingAndLineBreaks_ChangeTheBytesThatAreHashed()
    {
        using var page = new TextPage();
        await page.TypeAsync("a\r\nb");
        Assert.Equal(Sha256("a\nb"u8.ToArray()), page.HashOf(HashType.SHA256));

        page.Vm.SelectedLineBreak = LineBreak.CrLf;
        await page.Vm.Pending;
        Assert.Equal(Sha256("a\r\nb"u8.ToArray()), page.HashOf(HashType.SHA256));

        page.Vm.SelectedEncoding = page.Vm.Encodings.Single(e => e.Kind == TextEncodingKind.Utf16LittleEndian);
        await page.Vm.Pending;
        Assert.Equal(Sha256([0x61, 0, 0x0D, 0, 0x0A, 0, 0x62, 0]), page.HashOf(HashType.SHA256));
        Assert.Equal(new[] { "UTF-8", "UTF-16 LE", "UTF-16 BE" }, page.Vm.Encodings.Select(e => e.ToString()));
    }

    [AvaloniaFact]
    public async Task FollowsTheLetterCaseSetting()
    {
        using var page = new TextPage();
        page.Settings.Update(s => s.HashLetterCase = HexCase.Lower);
        await page.TypeAsync("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", page.HashOf(HashType.SHA256));
    }

    [AvaloniaFact]
    public async Task TypingQuickly_HashesOnlyTheLatestText()
    {
        using var page = new TextPage();
        page.Vm.Delay = TimeSpan.FromMilliseconds(50);

        page.Vm.Text = "a";
        page.Vm.Text = "ab";
        page.Vm.Text = "abc";
        await page.Vm.Pending;

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", page.HashOf(HashType.SHA256));
    }

    [AvaloniaFact]
    public async Task Copy_PutsTheWholeHashOnTheClipboard_AndEveryRowIsNamed()
    {
        using var page = new TextPage();
        await page.TypeAsync("abc");
        var sha256 = page.Vm.Results.Single(r => r.Algorithm == HashType.SHA256);

        await page.Vm.CopyCommand.ExecuteAsync(sha256);
        await page.Vm.CopyCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD" }, page.Clipboard.Texts);
        Assert.Equal(TestServices.Localization.Format("Lbl_AlgorithmHash", HashType.SHA256), sha256.HashName);
        Assert.Equal(TestServices.Localization.Format("Tip_CopyAlgorithmHash", HashType.SHA256), sha256.CopyName);
        Assert.All(page.Vm.Results, r => Assert.Contains(r.Algorithm.ToString(), r.HashName, StringComparison.Ordinal));
    }
}
