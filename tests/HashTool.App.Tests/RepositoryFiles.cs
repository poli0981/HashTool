using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HashTool.App.Tests;

/// <summary>Reads source files of the repository (translations, views) from the checkout the tests were built from.</summary>
internal static partial class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string AppDirectory => Path.Combine(Root, "src", "HashTool.App");

    public static string LangDirectory => Path.Combine(AppDirectory, "Lang");

    /// <summary>The neutral (English) resx and every culture resx, keyed by file culture ("" for neutral).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations { get; } = LoadTranslations();

    public static IReadOnlyDictionary<string, string> Neutral => Translations[""];

    public static IEnumerable<string> PlaceholdersOf(string value) =>
        Placeholder().Matches(value).Select(m => m.Value).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\{\d+(?:,[^}:]+)?(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HashTool.slnx"))) return directory.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root (HashTool.slnx).");
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadTranslations()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(LangDirectory, "Resources*.resx"))
        {
            var parts = Path.GetFileName(file).Split('.');
            var culture = parts.Length == 2 ? "" : parts[1];
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var data in XDocument.Load(file, LoadOptions.PreserveWhitespace).Root!.Elements("data"))
                entries[(string)data.Attribute("name")!] = (string?)data.Element("value") ?? "";
            result[culture] = entries;
        }

        return result;
    }
}
