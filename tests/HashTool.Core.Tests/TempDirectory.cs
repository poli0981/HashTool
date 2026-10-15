using System;
using System.IO;
using System.Text;

namespace HashTool.Core.Tests;

/// <summary>A temporary directory deleted when the test finishes.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Info = Directory.CreateTempSubdirectory("hashtool-tests-");

    public DirectoryInfo Info { get; }

    public string Path => Info.FullName;

    public string File(string relativePath, byte[] content)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public string File(string relativePath, string content) => File(relativePath, Encoding.UTF8.GetBytes(content));

    public void Dispose()
    {
        try
        {
            Info.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS cleans the temp folder eventually.
        }
    }
}
