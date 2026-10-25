using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Checksums;
using HashTool.Core.Hashing;

namespace HashTool.Cli;

/// <summary><c>hashtool verify</c> (like <c>sha256sum -c</c>) and <c>hashtool check</c> (one file against one hash).</summary>
internal static class VerifyCommand
{
    public static Command Create(TextWriter output, TextWriter error)
    {
        var files = new Argument<string[]>("checksum-files")
        {
            Description = "Checksum files (SHA256SUMS, *.sha256, *.md5, *.sfv, BSD-tagged lists...).",
            Arity = ArgumentArity.OneOrMore
        };
        var algorithm = HashToolCommand.AlgorithmOption("Algorithm to assume when neither the line nor the file name says which one.");
        algorithm.Arity = ArgumentArity.ExactlyOne;
        var quiet = new Option<bool>("--quiet", "-q") { Description = "Do not print a line for files that are OK." };
        var ignoreMissing = new Option<bool>("--ignore-missing") { Description = "Skip listed files that do not exist instead of failing." };

        var command = new Command("verify", "Verify files against checksum files, like 'sha256sum -c'.") { files, algorithm, quiet, ignoreMissing };
        command.SetAction((result, cancellationToken) => RunAsync(
            result.GetRequiredValue(files),
            result.GetValue(algorithm) is [var hint, ..] ? hint : null,
            result.GetValue(quiet),
            result.GetValue(ignoreMissing),
            output, error, cancellationToken));
        return command;
    }

    public static Command CreateCheck(TextWriter output, TextWriter error)
    {
        var file = new Argument<string>("file") { Description = "The file to check." };
        var expected = new Option<string>("--expected", "-e") { Description = "The expected hash (hexadecimal).", Required = true, HelpName = "hash" };
        var algorithm = HashToolCommand.AlgorithmOption("Algorithm of the expected hash (guessed from its length when omitted).");
        algorithm.Arity = ArgumentArity.ExactlyOne;

        var command = new Command("check", "Check one file against an expected hash.") { file, expected, algorithm };
        command.SetAction(async (result, cancellationToken) =>
        {
            var path = result.GetRequiredValue(file);
            var hash = HashMatcher.Normalize(result.GetRequiredValue(expected));
            IReadOnlyList<HashType> candidates = result.GetValue(algorithm) is [var chosen, ..]
                ? [chosen]
                : HashAlgorithms.CandidatesForHexLength(hash.Length);
            if (!HashMatcher.IsHex(hash) || candidates.Count == 0)
            {
                error.WriteLine($"hashtool: '{result.GetRequiredValue(expected)}' is not a hexadecimal hash of a supported length.");
                return HashToolCommand.UsageError;
            }

            var outcome = await CheckAsync(new HashEngine(), path, hash, candidates, cancellationToken).ConfigureAwait(false);
            output.WriteLine($"{path}: {outcome.Describe()}");
            if (outcome.Error is { } ex) HashToolCommand.ReportError(error, path, ex);
            return outcome.IsOk ? HashToolCommand.Success : HashToolCommand.Failure;
        });
        return command;
    }

    internal static async Task<int> RunAsync(IReadOnlyList<string> checksumFiles, HashType? hint, bool quiet, bool ignoreMissing,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var engine = new HashEngine();
        var exitCode = HashToolCommand.Success;
        int mismatched = 0, unreadable = 0, malformed = 0;

        foreach (var checksumFile in checksumFiles)
        {
            ChecksumDocument document;
            try
            {
                document = await ChecksumParser.ParseFileAsync(checksumFile, hint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                HashToolCommand.ReportError(error, checksumFile, ex);
                exitCode = HashToolCommand.UsageError;
                continue;
            }

            malformed += document.InvalidLines.Count;
            if (document.IsEmpty)
            {
                await error.WriteLineAsync($"hashtool: {checksumFile}: no properly formatted checksum lines found").ConfigureAwait(false);
                exitCode = HashToolCommand.UsageError;
                continue;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(checksumFile))!;
            foreach (var entry in document.Entries)
            {
                // A bare hash in "image.iso.sha256" belongs to "image.iso".
                var target = entry.FileName is null ? ChecksumFileNames.SidecarTarget(Path.GetFullPath(checksumFile)) : entry.ResolvePath(directory);
                var shown = entry.FileName ?? (target is null ? checksumFile : Path.GetFileName(target));
                if (target is null)
                {
                    await error.WriteLineAsync($"hashtool: {checksumFile}:{entry.LineNumber}: refusing to open network or device path '{shown}'")
                        .ConfigureAwait(false);
                    unreadable++;
                    continue;
                }

                if (ignoreMissing && !File.Exists(target)) continue;

                IReadOnlyList<HashType> candidates = entry.IsAlgorithmGuessed || entry.Algorithm is null
                    ? HashAlgorithms.CandidatesForHexLength(entry.Hash.Length)
                    : [entry.Algorithm.Value];
                var outcome = await CheckAsync(engine, target, entry.Hash, candidates, cancellationToken).ConfigureAwait(false);
                if (outcome.Error is not null) unreadable++;
                else if (!outcome.IsOk) mismatched++;
                if (!quiet || !outcome.IsOk) await output.WriteLineAsync($"{shown}: {outcome.Describe()}").ConfigureAwait(false);
                if (outcome.Error is { } ex) HashToolCommand.ReportError(error, shown, ex);
            }
        }

        if (malformed > 0)
            await error.WriteLineAsync($"hashtool: WARNING: {Count(malformed, "line is", "lines are")} improperly formatted").ConfigureAwait(false);
        if (unreadable > 0)
            await error.WriteLineAsync($"hashtool: WARNING: {Count(unreadable, "listed file", "listed files")} could not be read").ConfigureAwait(false);
        if (mismatched > 0)
            await error.WriteLineAsync($"hashtool: WARNING: {Count(mismatched, "computed checksum", "computed checksums")} did NOT match").ConfigureAwait(false);
        if (exitCode == HashToolCommand.Success && (mismatched > 0 || unreadable > 0)) exitCode = HashToolCommand.Failure;
        return exitCode;
    }

    private static string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    internal readonly record struct Outcome(HashMatch Match, HashType? Algorithm, Exception? Error)
    {
        public bool IsOk => Error is null && Match is HashMatch.Match or HashMatch.LegacyByteOrderMatch;

        public string Describe() => Error is not null
            ? "FAILED open or read"
            : Match switch
            {
                HashMatch.Match => "OK",
                HashMatch.LegacyByteOrderMatch => "OK (CRC32 written in reversed byte order)",
                HashMatch.LengthMismatch or HashMatch.InvalidExpected => "FAILED (not a valid hash for the algorithm)",
                _ => "FAILED"
            };
    }

    /// <summary>Hashes the file once with every candidate algorithm; any match counts.</summary>
    internal static async Task<Outcome> CheckAsync(HashEngine engine, string path, string expected, IReadOnlyList<HashType> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return new Outcome(HashMatch.LengthMismatch, null, null);
        HashResult[] results;
        try
        {
            results = await engine.ComputeFileAsync(path, candidates, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Outcome(HashMatch.Mismatch, null, ex);
        }

        var matches = results.Select(r => (Result: r, Match: HashMatcher.Compare(expected, r))).ToList();
        var best = matches.FirstOrDefault(m => m.Match == HashMatch.Match);
        if (best.Result is null) best = matches.FirstOrDefault(m => m.Match == HashMatch.LegacyByteOrderMatch);
        if (best.Result is null) best = matches[0];
        return new Outcome(best.Match, best.Result.Algorithm, null);
    }
}
