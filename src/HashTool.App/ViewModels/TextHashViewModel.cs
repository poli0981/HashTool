using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Services;
using Microsoft.Extensions.Logging;

namespace HashTool.App.ViewModels;

/// <summary>One algorithm's hash of the text on the "Hash Text" page.</summary>
public sealed partial class TextHashResult(HashType algorithm) : ObservableObject
{
    public HashType Algorithm { get; } = algorithm;

    [ObservableProperty] private string _hash = string.Empty;

    /// <summary>What screen readers call the hash box, such as "SHA256 hash".</summary>
    [ObservableProperty] private string _hashName = string.Empty;

    /// <summary>What screen readers call the copy button, such as "Copy SHA256 hash".</summary>
    [ObservableProperty] private string _copyName = string.Empty;
}

/// <summary>A text encoding to choose from. Its name is the same in every language, like the algorithm names.</summary>
public sealed record TextEncodingOption(TextEncodingKind Kind)
{
    public string Name => TextHashing.DisplayName(Kind);

    public override string ToString() => Name;
}

/// <summary>
/// The "Hash Text" page: the hash of typed or pasted text in every algorithm, recomputed shortly after each change. The
/// bytes hashed are exactly the text in the chosen encoding and line breaks (see <see cref="TextHashing"/>).
/// </summary>
public sealed partial class TextHashViewModel : ObservableObject
{
    private readonly HashEngine _engine;
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly LocalizationService _localizationService;
    private readonly ILogger<TextHashViewModel> _logger;
    private CancellationTokenSource? _pending;
    private long _byteCount;

    [ObservableProperty] private LocalizationProxy _localization;
    /// <summary>The text to hash; the text box can set null when it is cleared.</summary>
    [ObservableProperty] private string? _text = string.Empty;
    [ObservableProperty] private TextEncodingOption _selectedEncoding;
    [ObservableProperty] private LineBreak _selectedLineBreak = LineBreak.Lf;
    [ObservableProperty] private string _sizeText = string.Empty;
    [ObservableProperty] private bool _isRevealed;

    public TextHashViewModel(HashEngine engine, SettingsService settings, IClipboardService clipboard, LocalizationService localization,
        ILogger<TextHashViewModel> logger)
    {
        _engine = engine;
        _settings = settings;
        _clipboard = clipboard;
        _localizationService = localization;
        _logger = logger;
        _localization = new LocalizationProxy(localization);
        Encodings = Enum.GetValues<TextEncodingKind>().Select(kind => new TextEncodingOption(kind)).ToArray();
        _selectedEncoding = Encodings[0];
        Results = HashAlgorithms.All.Select(algorithm => new TextHashResult(algorithm)).ToArray();
        UpdateNames();

        localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(localization);
            UpdateNames();
        };
        // The letter case of the hashes is a setting.
        settings.Changed += (_, _) => Recompute(immediately: true);
        Recompute(immediately: true);
    }

    public IReadOnlyList<TextEncodingOption> Encodings { get; }

    public IReadOnlyList<LineBreak> LineBreaks { get; } = Enum.GetValues<LineBreak>();

    /// <summary>One row per algorithm, in the order the other pages list them.</summary>
    public IReadOnlyList<TextHashResult> Results { get; }

    /// <summary>How long to wait after a change before hashing, so typing does not hash every keystroke.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>The latest computation; tests wait for it.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    partial void OnTextChanged(string? value) => Recompute(immediately: false);

    partial void OnSelectedEncodingChanged(TextEncodingOption value) => Recompute(immediately: true);

    partial void OnSelectedLineBreakChanged(LineBreak value) => Recompute(immediately: true);

    [RelayCommand]
    private async Task CopyAsync(TextHashResult? result)
    {
        if (result is { Hash.Length: > 0 }) await _clipboard.SetTextAsync(result.Hash);
    }

    private void Recompute(bool immediately)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        Pending = ComputeAsync(immediately ? TimeSpan.Zero : Delay, _pending.Token);
    }

    private async Task ComputeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            var bytes = TextHashing.GetBytes(Text ?? string.Empty, SelectedEncoding.Kind, SelectedLineBreak);
            var letterCase = _settings.Current.HashLetterCase;
            var hashes = await Task.Run(async () =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return await _engine.ComputeStreamAsync(stream, HashAlgorithms.All, cancellationToken: cancellationToken);
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < Results.Count; i++) Results[i].Hash = hashes[i].ToHex(letterCase);
            _byteCount = bytes.LongLength;
            UpdateSizeText();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer change replaced this computation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hashing text failed");
        }
    }

    private void UpdateNames()
    {
        foreach (var result in Results)
        {
            result.HashName = _localizationService.Format("Lbl_AlgorithmHash", result.Algorithm);
            result.CopyName = _localizationService.Format("Tip_CopyAlgorithmHash", result.Algorithm);
        }

        UpdateSizeText();
    }

    private void UpdateSizeText() => SizeText = _localizationService.Format("Lbl_TextSize", ByteSize.Format(_byteCount));
}
