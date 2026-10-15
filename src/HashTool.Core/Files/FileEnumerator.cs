using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;

namespace HashTool.Core.Files;

/// <summary>A file found while expanding user input, with its size taken from the directory listing.</summary>
public readonly record struct FileEntry(string FullPath, long Length);

public sealed record FileEnumerationOptions
{
    /// <summary>Include files marked hidden or system (desktop.ini, .DS_Store…).</summary>
    public bool IncludeHidden { get; init; }

    public static FileEnumerationOptions Default { get; } = new();
}

/// <summary>
/// Expands dropped or picked paths into files. Directories are walked recursively without following symbolic links or
/// junctions (so link cycles cannot loop forever), and unreadable directories are skipped instead of aborting the scan.
/// </summary>
public static class FileEnumerator
{
    public static IEnumerable<FileEntry> Expand(IEnumerable<string> paths, FileEnumerationOptions? options = null,
        Action<string, Exception>? onError = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        options ??= FileEnumerationOptions.Default;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (Directory.Exists(path))
            {
                foreach (var entry in EnumerateDirectory(path, options, onError, cancellationToken)) yield return entry;
                continue;
            }

            FileEntry? single = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) single = new FileEntry(info.FullName, info.Length);
                else onError?.Invoke(path, new FileNotFoundException("File not found.", path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                onError?.Invoke(path, ex);
            }

            if (single is { } file) yield return file;
        }
    }

    public static IEnumerable<FileEntry> EnumerateDirectory(string directory, FileEnumerationOptions? options = null,
        Action<string, Exception>? onError = null, CancellationToken cancellationToken = default)
    {
        options ??= FileEnumerationOptions.Default;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = options.IncludeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerator<FileEntry> enumerator;
        try
        {
            enumerator = new FileSystemEnumerable<FileEntry>(Path.GetFullPath(directory),
                    static (ref FileSystemEntry entry) => new FileEntry(entry.ToFullPath(), entry.Length), enumerationOptions)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,
                ShouldRecursePredicate = static (ref FileSystemEntry entry) => !IsLink(ref entry)
            }
                .GetEnumerator();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onError?.Invoke(directory, ex);
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool moved;
                try
                {
                    moved = enumerator.MoveNext();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    onError?.Invoke(directory, ex);
                    yield break;
                }

                if (!moved) yield break;
                yield return enumerator.Current;
            }
        }
    }

    /// <summary>
    /// Symbolic links and junctions are not followed: they can point at a parent and recurse forever. Other reparse points
    /// (OneDrive and other cloud-file placeholders) are normal folders and are walked.
    /// </summary>
    private static bool IsLink(ref FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return false;
        try
        {
            return new DirectoryInfo(entry.ToFullPath()).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
