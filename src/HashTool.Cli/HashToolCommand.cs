using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;

namespace HashTool.Cli;

/// <summary>
/// <c>hashtool</c>: compute and verify hashes from the command line. Exit codes follow coreutils' <c>sha256sum</c>:
/// 0 = success, 1 = a file failed or did not match, 2 = usage error or unreadable checksum file.
/// </summary>
public static class HashToolCommand
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var parseResult = Build(output, error).Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            // Short message and exit code 2, like coreutils, instead of the full help text.
            foreach (var parseError in parseResult.Errors) error.WriteLine($"hashtool: {parseError.Message}");
            error.WriteLine("Try 'hashtool --help' for more information.");
            return Task.FromResult(UsageError);
        }

        return parseResult.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
    }

    internal static RootCommand Build(TextWriter output, TextWriter error)
    {
        var root = new RootCommand($"Hash Tool {Version} - compute and verify file hashes and checksum files (SHA256SUMS, .md5, .sfv...).");
        root.Subcommands.Add(HashCommand.Create(output, error));
        root.Subcommands.Add(VerifyCommand.Create(output, error));
        root.Subcommands.Add(VerifyCommand.CreateCheck(output, error));
        root.Subcommands.Add(CreateAlgorithmsCommand(output));
        return root;
    }

    internal static string Version =>
        typeof(HashToolCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary><c>-a sha256 -a md5</c> or <c>-a sha256,md5</c>; names as listed by <c>hashtool algorithms</c>.</summary>
    internal static Option<HashType[]> AlgorithmOption(string description) => new("--algorithm", "-a")
    {
        Description = description,
        HelpName = "name",
        Arity = ArgumentArity.OneOrMore,
        CustomParser = ParseAlgorithms
    };

    private static HashType[] ParseAlgorithms(ArgumentResult result)
    {
        var algorithms = new List<HashType>();
        foreach (var token in result.Tokens)
        {
            foreach (var name in token.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (HashAlgorithms.TryParse(name, out var algorithm)) algorithms.Add(algorithm);
                else result.AddError($"Unknown algorithm '{name}'. Run 'hashtool algorithms' to list the supported ones.");
            }
        }

        return [.. algorithms.Distinct()];
    }

    private static Command CreateAlgorithmsCommand(TextWriter output)
    {
        var command = new Command("algorithms", "List the supported hash algorithms.");
        command.SetAction(_ =>
        {
            foreach (var algorithm in HashAlgorithms.All)
            {
                var note = algorithm.IsInsecure() ? "  (not collision resistant; use for integrity checks only)" : "";
                output.WriteLine($"{algorithm.ToString().ToLowerInvariant(),-10} {HashAlgorithms.HexLength(algorithm),3} hex digits{note}");
            }

            return Success;
        });
        return command;
    }

    internal static void ReportError(TextWriter error, string path, Exception exception) =>
        error.WriteLine($"hashtool: {path}: {Describe(exception)}");

    internal static string Describe(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "No such file or directory",
        UnauthorizedAccessException => "Permission denied",
        _ => exception.Message
    };

    /// <summary>
    /// Path as written to checksum output: relative to <paramref name="relativeTo"/> when given, otherwise absolute. Windows
    /// separators become '/', so lines need no GNU escaping and the files also work with coreutils.
    /// </summary>
    internal static string DisplayPath(string fullPath, string? relativeTo)
    {
        var path = relativeTo is null ? fullPath : Path.GetRelativePath(relativeTo, fullPath);
        return Portable(path);
    }

    internal static string Portable(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
}

