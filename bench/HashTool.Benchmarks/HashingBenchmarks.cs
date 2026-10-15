using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HashTool.Core;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;

namespace HashTool.Benchmarks;

/// <summary>Large-file throughput of <see cref="HashEngine"/> against the one-line BCL approach.</summary>
[MemoryDiagnoser]
public class LargeFileBenchmarks
{
    private readonly HashEngine _engine = new();
    private string _file = null!;

    [Params(256)]
    public int SizeMB { get; set; }

    [GlobalSetup]
    public void Setup() => _file = TestFiles.Create($"large-{SizeMB}", (long)SizeMB * ByteSize.OneMB);

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_file);

    [Benchmark(Baseline = true, Description = "SHA256.HashData(FileStream)")]
    public byte[] Bcl_Sha256()
    {
        using var stream = File.OpenRead(_file);
        return SHA256.HashData(stream);
    }

    [Benchmark]
    public Task<HashResult> Sha256() => _engine.ComputeFileAsync(_file, HashType.SHA256);

    [Benchmark]
    public Task<HashResult> Blake3() => _engine.ComputeFileAsync(_file, HashType.BLAKE3);

    [Benchmark]
    public Task<HashResult> XxHash3() => _engine.ComputeFileAsync(_file, HashType.XxHash3);

    [Benchmark]
    public Task<HashResult> Crc32() => _engine.ComputeFileAsync(_file, HashType.CRC32);

    /// <summary>One read of the file for three algorithms (1.x read the file once per algorithm).</summary>
    [Benchmark]
    public Task<HashResult[]> Sha256_Md5_Crc32_OnePass() => _engine.ComputeFileAsync(_file, [HashType.SHA256, HashType.MD5, HashType.CRC32]);
}

/// <summary>Many small files: sequential hashing against <see cref="BatchProcessor"/>.</summary>
[MemoryDiagnoser]
public class SmallFilesBenchmarks
{
    private readonly HashEngine _engine = new();
    private string _directory = null!;
    private List<string> _files = null!;

    [Params(2000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _directory = Directory.CreateTempSubdirectory("hashtool-bench-small-").FullName;
        _files = [];
        var buffer = new byte[16 * ByteSize.OneKB];
        for (var i = 0; i < Count; i++)
        {
            Random.Shared.NextBytes(buffer);
            var path = Path.Combine(_directory, $"{i:D5}.bin");
            File.WriteAllBytes(path, buffer);
            _files.Add(path);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [Benchmark(Baseline = true)]
    public async Task Sequential()
    {
        foreach (var file in _files) await _engine.ComputeFileAsync(file, HashType.SHA256);
    }

    [Benchmark]
    public Task Batch() => new BatchProcessor(_engine).RunAsync(_files,
        f => new HashJob(f, 16 * ByteSize.OneKB, [HashType.SHA256]), NullObserver.Instance, CancellationToken.None);

    private sealed class NullObserver : IHashJobObserver<string>
    {
        public static readonly NullObserver Instance = new();

        public void OnStarted(string item)
        {
        }

        public void OnProgress(string item, long bytesRead)
        {
        }

        public void OnCompleted(string item, HashJobResult result)
        {
        }
    }
}

internal static class TestFiles
{
    public static string Create(string name, long size)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hashtool-bench-{name}.bin");
        if (File.Exists(path) && new FileInfo(path).Length == size) return path;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 0);
        var buffer = new byte[4 * ByteSize.OneMB];
        for (long written = 0; written < size; written += buffer.Length)
        {
            Random.Shared.NextBytes(buffer);
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, size - written));
        }

        return path;
    }
}
