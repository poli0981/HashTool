using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Checksums;
using HashTool.Core.Files;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;

namespace HashTool.Cli;

/// <summary><c>hashtool hash</c>: prints or writes checksum lines for files and folders.</summary>
internal static class HashCommand
{
    private sealed record Input(string FullPath, string Display);

    public static Command Create(TextWriter output, TextWriter error)
    {
        var paths = new Argument<string[]>("paths") { Description = "Files to hash (folders need --recursive).", Arity = ArgumentArity.OneOrMore };
        var algorithms = HashToolCommand.AlgorithmOption("Algorithm(s) to use: sha256 (default), sha1, md5, sha384, sha512, blake3, xxhash3, ...");
        var recursive = new Option<bool>("--recursive", "-r") { Description = "Hash every file inside the given folders." };
        var hidden = new Option<bool>("--hidden") { Description = "Include hidden and system files when walking folders." };
        var format = new Option<ExportFormat?>("--format", "-f")
        {
            Description = "Output format: gnu (sha256sum style, default for one algorithm), bsd (tagged, default for several), sfv (CRC32 only), json or csv."
        };
        var outputFile = new Option<FileInfo?>("--output", "-o")
        { Description = "Write to this file instead of the console. Paths inside are relative to its folder.", HelpName = "file" };
        var upper = new Option<bool>("--uppercase", "-u") { Description = "Print hexadecimal digits in upper case." };
        var jobs = new Option<int>("--jobs", "-j") { Description = "Files hashed in parallel (0 = automatic, 1 = one at a time for hard disks).", HelpName = "count" };

        var command = new Command("hash", "Compute hashes of files and print them as checksum lines.")
        {
            paths, algorithms, recursive, hidden, format, outputFile, upper, jobs
        };
        command.SetAction((result, cancellationToken) => RunAsync(new Request(
            result.GetRequiredValue(paths),
            result.GetValue(algorithms) is { Length: > 0 } chosen ? chosen : [HashType.SHA256],
            result.GetValue(recursive),
            result.GetValue(hidden),
            result.GetValue(format),
            result.GetValue(outputFile),
            result.GetValue(upper) ? HexCase.Upper : HexCase.Lower,
            result.GetValue(jobs)), output, error, cancellationToken));
        return command;
    }

    internal sealed record Request(
        IReadOnlyList<string> Paths,
        IReadOnlyList<HashType> Algorithms,
        bool Recursive,
        bool IncludeHidden,
        ExportFormat? Format,
        FileInfo? OutputFile,
        HexCase LetterCase,
        int Jobs);

    internal static async Task<int> RunAsync(Request request, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var format = request.Format ?? (request.Algorithms.Count == 1 ? ExportFormat.Gnu : ExportFormat.Bsd);
        if (format == ExportFormat.Gnu && request.Algorithms.Count > 1)
        {
            error.WriteLine("hashtool: the gnu format holds a single algorithm; use --format bsd, json or csv for several.");
            return HashToolCommand.UsageError;
        }

        if (format == ExportFormat.Sfv && request.Algorithms.Any(a => a != HashType.CRC32))
        {
            error.WriteLine("hashtool: sfv files contain CRC32 only; add -a crc32.");
            return HashToolCommand.UsageError;
        }

        var failures = 0;
        var relativeTo = request.OutputFile?.DirectoryName;
        var inputs = Expand(request, relativeTo, error, ref failures, cancellationToken);

        var engine = new HashEngine();
        var results = new HashResult[]?[inputs.Count];
        var errors = new Exception?[inputs.Count];
        var parallelism = request.Jobs > 0 ? request.Jobs : new BatchOptions().EffectiveParallelism;
        await Parallel.ForEachAsync(Enumerable.Range(0, inputs.Count),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (i, token) =>
            {
                try
                {
                    results[i] = await engine.ComputeFileAsync(inputs[i].FullPath, request.Algorithms, null, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors[i] = ex;
                }
            }).ConfigureAwait(false);

        var lines = new List<ChecksumLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            if (errors[i] is { } ex)
            {
                HashToolCommand.ReportError(error, inputs[i].Display, ex);
                failures++;
                continue;
            }

            var size = SafeLength(inputs[i].FullPath);
            lines.AddRange(results[i]!.Select(r => new ChecksumLine(inputs[i].Display, r.Algorithm, r.ToHex(request.LetterCase), size)));
        }

        if (request.OutputFile is { } file)
        {
            try
            {
                await File.WriteAllTextAsync(file.FullName, ChecksumWriter.Write(lines, format, request.LetterCase), new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                HashToolCommand.ReportError(error, file.FullName, ex);
                return HashToolCommand.Failure;
            }
        }
        else
        {
            ChecksumWriter.Write(output, lines, format, request.LetterCase);
        }

        return failures == 0 ? HashToolCommand.Success : HashToolCommand.Failure;
    }

    private static List<Input> Expand(Request request, string? relativeTo, TextWriter error, ref int failures, CancellationToken cancellationToken)
    {
        var inputs = new List<Input>();
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var options = new FileEnumerationOptions { IncludeHidden = request.IncludeHidden };
        var errors = 0;
        foreach (var path in request.Paths)
        {
            // Relative input stays relative in the output, like sha256sum; with --output it is relative to that file.
            var displayBase = relativeTo ?? (Path.IsPathRooted(path) ? null : Environment.CurrentDirectory);
            if (Directory.Exists(path))
            {
                if (!request.Recursive)
                {
                    error.WriteLine($"hashtool: {path}: is a directory (use --recursive to hash the files inside)");
                    errors++;
                    continue;
                }

                foreach (var entry in FileEnumerator.EnumerateDirectory(path, options, (p, ex) =>
                         {
                             HashToolCommand.ReportError(error, p, ex);
                             errors++;
                         }, cancellationToken))
                {
                    if (seen.Add(entry.FullPath)) inputs.Add(new Input(entry.FullPath, HashToolCommand.DisplayPath(entry.FullPath, displayBase)));
                }
            }
            else if (File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) inputs.Add(new Input(full, relativeTo is null ? HashToolCommand.Portable(path) : HashToolCommand.DisplayPath(full, relativeTo)));
            }
            else
            {
                error.WriteLine($"hashtool: {path}: No such file or directory");
                errors++;
            }
        }

        failures += errors;
        return inputs;
    }

    private static long? SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
