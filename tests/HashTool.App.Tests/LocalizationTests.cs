using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using HashTool.App.Services;
using HashTool.Core.Configuration;
using Xunit;

namespace HashTool.App.Tests;

/// <summary>
/// Guards the 66 translations: same keys and placeholders as English, no stray whitespace, every language wired up in
/// Languages.json, the project file and the build output, and no key that the code uses but no file defines (or the reverse).
/// </summary>
public sealed partial class LocalizationTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Cultures =
        RepositoryFiles.Translations.Where(t => t.Key.Length > 0).ToDictionary(t => t.Key, t => t.Value);

    private static IReadOnlyList<(string Name, string Code)> LanguagesJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.LangDirectory, "Languages.json")));
        return document.RootElement.EnumerateArray().Select(e => (e.GetProperty("Name").GetString()!, e.GetProperty("Code").GetString()!)).ToList();
    }

    [Fact]
    public void EveryCultureHasExactlyTheEnglishKeys()
    {
        var problems = Cultures.SelectMany(c =>
            RepositoryFiles.Neutral.Keys.Except(c.Value.Keys).Select(k => $"{c.Key}: missing {k}")
                .Concat(c.Value.Keys.Except(RepositoryFiles.Neutral.Keys).Select(k => $"{c.Key}: unknown {k}")));

        Assert.Empty(problems);
    }

    [Fact]
    public void PlaceholdersMatchEnglish()
    {
        var problems = Cultures.SelectMany(c => c.Value
            .Where(e => RepositoryFiles.Neutral.ContainsKey(e.Key) &&
                        !RepositoryFiles.PlaceholdersOf(e.Value).SequenceEqual(RepositoryFiles.PlaceholdersOf(RepositoryFiles.Neutral[e.Key])))
            .Select(e => $"{c.Key}: {e.Key} = {e.Value}"));

        Assert.Empty(problems);
    }

    [Fact]
    public void ValuesAreNotEmptyOrIndented()
    {
        var problems = RepositoryFiles.Translations.SelectMany(c => c.Value
            .Where(e => string.IsNullOrWhiteSpace(e.Value) || e.Value != e.Value.Trim() ||
                        e.Value.Split('\n').Any(line => line != line.Trim()))
            .Select(e => $"{(c.Key.Length == 0 ? "neutral" : c.Key)}: {e.Key}"));

        Assert.Empty(problems);
    }

    [Fact]
    public void LanguagesJsonResxFilesAndProjectAgree()
    {
        var codes = LanguagesJson().Select(l => l.Code).ToList();
        var project = File.ReadAllText(Path.Combine(RepositoryFiles.AppDirectory, "HashTool.App.csproj"));
        var declared = ProjectCulture().Matches(project).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("en-US", codes); // falls back to the neutral English resources
        Assert.Equal(codes.Where(c => c != "en-US").Order(StringComparer.OrdinalIgnoreCase),
            Cultures.Keys.Select(f => codes.First(c => c.Equals(f, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Cultures.Keys.Order(StringComparer.OrdinalIgnoreCase), declared.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (file, culture) in declared)
            Assert.Equal(codes.First(c => c.Equals(file, StringComparison.OrdinalIgnoreCase)), culture);
    }

    [Fact]
    public void EverySatelliteAssemblyLoads()
    {
        var resources = new ResourceManager("HashTool.App.Lang.Resources", typeof(App).Assembly);
        foreach (var (_, code) in LanguagesJson().Where(l => l.Code != "en-US"))
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            Assert.True(set is not null, $"No resources for {code} ({SatelliteDiagnostics(culture)})");
            Assert.Equal(Cultures[code.ToLowerInvariant()]["Menu_Create"], set.GetString("Menu_Create"));
        }
    }

    private static string SatelliteDiagnostics(CultureInfo culture)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, culture.Name);
        try
        {
            var satellite = typeof(App).Assembly.GetSatelliteAssembly(culture);
            return $"culture '{culture.Name}', satellite contains: {string.Join(", ", satellite.GetManifestResourceNames())}";
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
        {
            return $"culture '{culture.Name}', folder {(Directory.Exists(folder) ? "exists" : "missing")}: {ex.GetType().Name}";
        }
    }

    [Fact]
    public void EveryKeyUsedByTheCodeExists()
    {
        var missing = UsedKeys().Where(k => !RepositoryFiles.Neutral.ContainsKey(k));

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryKeyIsUsed()
    {
        var used = UsedKeys().ToHashSet(StringComparer.Ordinal);
        var unused = RepositoryFiles.Neutral.Keys.Where(k => !used.Contains(k));

        Assert.Empty(unused);
    }

    private static IEnumerable<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(RepositoryFiles.AppDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            var text = Path.GetExtension(file) switch
            {
                ".axaml" or ".cs" => File.ReadAllText(file),
                _ => null
            };
            if (text is null) continue;
            foreach (Match match in XamlKey().Matches(text)) keys.Add(match.Groups[1].Value);
            foreach (Match match in CodeKey().Matches(text)) keys.Add(match.Groups[1].Value);
        }

        // Built at runtime: $"Theme_{style}".
        foreach (var style in Enum.GetValues<AppThemeStyle>()) keys.Add($"Theme_{style}");
        return keys;
    }

    [GeneratedRegex(@"Localization\[(AppTitle|[A-Za-z]+_[A-Za-z0-9_]+)\]")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"""((?:AppTitle|(?:About|Btn|Dev|Dialog|Doc|EmptyState|Error|Export|FileType|Filter|Font|Help|Lang|Lbl|Menu|Msg|Settings|Shell|Size|Status|Theme|Tip|Title|Unit|Watermark)_[A-Za-z0-9_]+))""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"<EmbeddedResource Update=""Lang\\Resources\.([^""]+)\.resx"" Culture=""([^""]+)"" />")]
    private static partial Regex ProjectCulture();

    // ------------------------------------------------------------------ system language detection

    private static IReadOnlyList<LanguageItem> Languages() => LanguagesJson().Select(l => new LanguageItem(l.Name, l.Code)).ToList();

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("en-AU", "en-GB")]
    [InlineData("en-CA", "en-US")]
    [InlineData("vi-VN", "vi-VN")]
    [InlineData("zh-CN", "zh-Hans-CN")]
    [InlineData("zh-SG", "zh-Hans-SG")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es-MX", "es-419")]
    [InlineData("es-AR", "es-419")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("de-LU", "de-DE")]
    [InlineData("de-AT", "de-AT")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("fi-FI", "fi-FI")]
    [InlineData("fil-PH", "fil-PH")]
    [InlineData("el-GR", "el-GR")]
    [InlineData("nn-NO", "nb-NO")]
    [InlineData("sr-Latn-RS", "sr-Cyrl-RS")]
    [InlineData("ar-EG", "ar")]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("sw-KE", "en-US")]
    public void DetectSystemLanguage_MapsCultures(string system, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(system);

        Assert.Equal(expected, LocalizationService.DetectSystemLanguage(Languages(), culture, culture));
    }

    [Fact]
    public void DetectSystemLanguage_FallsBackToTheRegionalFormat()
    {
        Assert.Equal("ja-JP", LocalizationService.DetectSystemLanguage(Languages(), CultureInfo.GetCultureInfo("sw-KE"), CultureInfo.GetCultureInfo("ja-JP")));
    }
}
