using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HashTool.App.Infrastructure.Logging;

/// <summary>Replaces the user's profile and temp folders in log text so shared logs do not reveal user names.</summary>
public sealed class PathSanitizer
{
    private readonly (string Path, string Placeholder)[] _replacements;

    public PathSanitizer()
    {
        var replacements = new List<(string, string)>();
        Add(replacements, Path.GetTempPath(), "[TEMP]");
        Add(replacements, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[USER_PROFILE]");
        _replacements = replacements.OrderByDescending(r => r.Item1.Length).ToArray();
    }

    public static PathSanitizer Shared { get; } = new();

    public string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        foreach (var (path, placeholder) in _replacements)
            text = text.Replace(path, placeholder, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static void Add(List<(string, string)> list, string? path, string placeholder)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length > 1) list.Add((trimmed, placeholder));
    }
}
