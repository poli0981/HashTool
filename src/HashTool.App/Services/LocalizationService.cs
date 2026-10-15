using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HashTool.App.Services;

/// <summary>A selectable UI language. <see cref="Code"/> is "auto" for the system language.</summary>
public sealed partial class LanguageItem(string name, string code) : ObservableObject
{
    public const string AutoCode = "auto";

    [ObservableProperty] private string _name = name;

    public string Code { get; } = code;

    public bool IsAuto => Code == AutoCode;

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is LanguageItem other && string.Equals(other.Code, Code, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Code);
}

internal sealed record LanguageEntry(string Name, string Code);

/// <summary>
/// Looks up UI strings and owns the current UI culture. XAML binds to <c>Localization[Key]</c>; when the language changes the
/// <see cref="LanguageChanged"/> event fires and view models swap their <see cref="LocalizationProxy"/> to refresh bindings.
/// </summary>
public sealed partial class LocalizationService : ObservableObject
{
    private static readonly Dictionary<string, string> PreferredVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "zh-Hans-CN",
        ["en"] = "en-US",
        ["es"] = "es-ES",
        ["de"] = "de-DE",
        ["fr"] = "fr-FR",
        ["pt"] = "pt-BR",
        ["el"] = "el-GR",
        ["nl"] = "nl-NL",
        ["fi"] = "fi-FI",
        ["sr"] = "sr-Cyrl-RS",
        ["bs"] = "bs-Latn-BA",
        ["nb"] = "nb-NO",
        ["nn"] = "nb-NO",
        ["no"] = "nb-NO"
    };

    private readonly ResourceManager _resources = new("HashTool.App.Lang.Resources", typeof(LocalizationService).Assembly);
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedMissing = new(StringComparer.Ordinal);
    private readonly CultureInfo _systemUiCulture;
    private readonly CultureInfo _systemCulture;
    private readonly ILogger _logger;

    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;
    [ObservableProperty] private LanguageItem _selectedLanguage;

    public LocalizationService(ILogger<LocalizationService>? logger = null)
        : this(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, logger)
    {
    }

    public LocalizationService(CultureInfo systemUiCulture, CultureInfo systemCulture, ILogger<LocalizationService>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _systemUiCulture = systemUiCulture;
        _systemCulture = systemCulture;
        Languages = LoadLanguages();
        AvailableLanguages = [new LanguageItem("Auto (System)", LanguageItem.AutoCode), .. Languages];
        _selectedLanguage = AvailableLanguages[0];
        Culture = ApplyCulture(DetectSystemLanguage(Languages, systemUiCulture, systemCulture));
        RefreshAutoName();
        Current = this;
    }

    /// <summary>The instance used by XAML converters that cannot receive services.</summary>
    public static LocalizationService? Current { get; private set; }

    /// <summary>The concrete languages (without the "auto" entry).</summary>
    public IReadOnlyList<LanguageItem> Languages { get; }

    /// <summary>"Auto (System)" followed by every language.</summary>
    public List<LanguageItem> AvailableLanguages { get; }

    public CultureInfo Culture { get; private set; }

    /// <summary>Raised on the UI thread after the language changed.</summary>
    public event EventHandler? LanguageChanged;

    public string this[string key] => Get(key);

    public string Get(string key) => _cache.GetOrAdd(key, Lookup);

    /// <summary>Formats a localized pattern; a broken translation falls back to the English pattern instead of throwing.</summary>
    public string Format(string key, params object?[] args)
    {
        var pattern = Get(key);
        try
        {
            return string.Format(Culture, pattern, args);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Translation of {Key} for {Culture} has invalid placeholders", key, Culture.Name);
            var neutral = _resources.GetString(key, CultureInfo.InvariantCulture) ?? pattern;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, neutral, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }
    }

    /// <summary>Whether the satellite assembly for <paramref name="code"/> is present (smoke tests, diagnostics).</summary>
    public bool HasResourcesFor(string code)
    {
        try
        {
            return _resources.GetResourceSet(CultureInfo.GetCultureInfo(code), createIfNotExists: true, tryParents: false) is not null;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Finds the language item for a saved code (case-insensitive); unknown codes fall back to auto.</summary>
    public LanguageItem Find(string? code) =>
        AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? AvailableLanguages[0];

    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        var code = value.IsAuto ? DetectSystemLanguage(Languages, _systemUiCulture, _systemCulture) : value.Code;
        Culture = ApplyCulture(code);
        _cache.Clear();
        RefreshAutoName();
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The language code a system culture maps to, e.g. zh-TW → zh-Hant, es-MX → es-419, fi → fi-FI.</summary>
    public static string DetectSystemLanguage(IReadOnlyList<LanguageItem> languages, CultureInfo uiCulture, CultureInfo culture)
    {
        var codes = languages.Select(l => l.Code).ToList();
        foreach (var candidate in new[] { uiCulture, culture })
        {
            var match = Match(codes, candidate);
            if (match is not null) return match;
        }

        return codes.FirstOrDefault(c => c.Equals("en-US", StringComparison.OrdinalIgnoreCase)) ?? codes.FirstOrDefault() ?? "en-US";
    }

    private static string? Match(List<string> codes, CultureInfo culture)
    {
        if (culture.Equals(CultureInfo.InvariantCulture)) return null;

        // 1. Exact culture or one of its parents (sr-Latn-RS → sr-Latn → sr …).
        for (var c = culture; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            var exact = codes.FirstOrDefault(code => code.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        var name = culture.Name;
        var language = culture.TwoLetterISOLanguageName;

        // 2. Script and region specific rules.
        if (language == "zh")
        {
            var traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                              name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                              name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                              name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            var wanted = traditional ? "zh-Hant" : name.EndsWith("-SG", StringComparison.OrdinalIgnoreCase) ? "zh-Hans-SG" : "zh-Hans-CN";
            if (codes.Contains(wanted, StringComparer.OrdinalIgnoreCase)) return wanted;
        }

        if (language == "es" && !name.Equals("es-ES", StringComparison.OrdinalIgnoreCase) && name.Contains('-') &&
            codes.Contains("es-419", StringComparer.OrdinalIgnoreCase))
            return "es-419";

        // Commonwealth English uses British spelling.
        if (language == "en" && name.Split('-')[^1].ToUpperInvariant() is "AU" or "NZ" or "IE" or "ZA" or "MT" or "CY" &&
            codes.Contains("en-GB", StringComparer.OrdinalIgnoreCase))
            return "en-GB";

        // 3. Same language, preferred variant first, then any variant whose language subtag matches exactly.
        if (PreferredVariants.TryGetValue(language, out var preferred) && codes.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            return preferred;

        return codes.FirstOrDefault(code => LanguageSubtag(code).Equals(language, StringComparison.OrdinalIgnoreCase) ||
                                            LanguageSubtag(code).Equals(culture.ThreeLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
    }

    private static string LanguageSubtag(string code)
    {
        var dash = code.IndexOf('-');
        return dash < 0 ? code : code[..dash];
    }

    private CultureInfo ApplyCulture(string code)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogWarning(ex, "Culture {Code} is not available on this system; using English", code);
            culture = CultureInfo.GetCultureInfo("en-US");
        }

        // Set the defaults (for thread-pool work) and the current thread. Doing this from synchronous code matters:
        // culture changes made inside an async method are rolled back when it completes.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        FlowDirection = culture.TextInfo.IsRightToLeft || IsRightToLeftLanguage(culture) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        return culture;
    }

    /// <summary>ICU on some systems lacks data for Dhivehi, so check the language too.</summary>
    private static bool IsRightToLeftLanguage(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName is "ar" or "he" or "fa" or "ps" or "dv" or "ur" or "yi" or "ug" or "sd" or "ku";

    private string Lookup(string key)
    {
        string? value = null;
        try
        {
            value = _resources.GetString(key, Culture);
            if (string.IsNullOrEmpty(value)) value = _resources.GetString(key, CultureInfo.InvariantCulture);
        }
        catch (MissingManifestResourceException ex)
        {
            _logger.LogError(ex, "Resources are missing from the assembly");
        }

        if (!string.IsNullOrEmpty(value)) return value;
        if (_reportedMissing.TryAdd(key, 0)) _logger.LogWarning("Missing translation key {Key}", key);
        return $"[{key}]";
    }

    private void RefreshAutoName() =>
        AvailableLanguages[0].Name = _resources.GetString("Lang_Auto", Culture) ?? "Auto (System)";

    private static IReadOnlyList<LanguageItem> LoadLanguages()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HashTool.App.Lang.Languages.json")
                           ?? throw new FileNotFoundException("Languages.json is not embedded in the assembly.");
        var items = JsonSerializer.Deserialize(stream, LanguagesJsonContext.Default.ListLanguageEntry) ?? [];
        return items.Where(i => !string.IsNullOrWhiteSpace(i.Code) && i.Code != LanguageItem.AutoCode)
            .Select(i => new LanguageItem(i.Name, i.Code)).ToArray();
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<LanguageEntry>))]
internal sealed partial class LanguagesJsonContext : JsonSerializerContext;
