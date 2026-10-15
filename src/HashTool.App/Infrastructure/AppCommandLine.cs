using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HashTool.App.Infrastructure;

/// <summary>Which page incoming files should open in.</summary>
public enum ActivationVerb
{
    /// <summary>Checksum files go to Verify, everything else to Create.</summary>
    Auto,
    Create,
    Verify
}

/// <summary>Files handed to the app by the command line, Explorer, another instance or macOS "Open With".</summary>
public sealed record ActivationRequest(ActivationVerb Verb, IReadOnlyList<string> Paths);

/// <summary>
/// <c>CheckHash [--create|--verify] [--files-from list.txt] [paths…]</c>, plus <c>--smoke-test</c> for CI and
/// <c>--wait-pid</c> used when restarting with administrator rights.
/// </summary>
public sealed record AppCommandLine(
    ActivationVerb Verb,
    IReadOnlyList<string> Paths,
    bool SmokeTest,
    string? SmokeOutput,
    string? ExpectedVersion,
    int? WaitPid)
{
    public ActivationRequest? ToRequest() => Paths.Count == 0 ? null : new ActivationRequest(Verb, Paths);

    public static AppCommandLine Parse(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        workingDirectory ??= Environment.CurrentDirectory;
        var verb = ActivationVerb.Auto;
        var paths = new List<string>();
        var smoke = false;
        string? smokeOutput = null, expected = null;
        int? waitPid = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Next() => i + 1 < args.Count ? args[++i] : null;
            switch (arg.ToLowerInvariant())
            {
                case "--create":
                    verb = ActivationVerb.Create;
                    break;
                case "--verify":
                    verb = ActivationVerb.Verify;
                    break;
                case "--files-from":
                    if (Next() is { } list && File.Exists(list))
                    {
                        paths.AddRange(File.ReadAllLines(list).Where(l => !string.IsNullOrWhiteSpace(l)));
                        if (IsTemporaryFileList(list)) TryDelete(list);
                    }

                    break;
                case "--smoke-test":
                    smoke = true;
                    break;
                case "--smoke-out":
                    smokeOutput = Next();
                    break;
                case "--expect-version":
                    expected = Next();
                    break;
                case "--wait-pid":
                    if (int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) waitPid = pid;
                    break;
                default:
                    if (!arg.StartsWith("--", StringComparison.Ordinal)) paths.Add(arg);
                    break;
            }
        }

        var resolved = paths.Select(p => Path.GetFullPath(Path.Combine(workingDirectory, p)))
            .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase).ToList();
        return new AppCommandLine(verb, resolved, smoke, smokeOutput, expected, waitPid);
    }

    /// <summary>
    /// True only for the list <see cref="Elevation.RestartElevated"/> writes (<c>%TEMP%\hashtool-&lt;guid&gt;.txt</c>), so a
    /// crafted command line cannot make the app delete any other file.
    /// </summary>
    internal static bool IsTemporaryFileList(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(full);
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                   OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) &&
               Path.GetExtension(full) == ".txt" &&
               name.StartsWith("hashtool-", StringComparison.Ordinal) &&
               Guid.TryParseExact(name["hashtool-".Length..], "N", out _);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
