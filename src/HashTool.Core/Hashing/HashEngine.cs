using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HashTool.Core.Hashing;

/// <summary>Tuning knobs for <see cref="HashEngine"/>.</summary>
public sealed record HashEngineOptions
{
    /// <summary>Read size for files smaller than <see cref="LargeFileThreshold"/>.</summary>
    public int BufferSize { get; init; } = ByteSize.OneMB;

    /// <summary>Read size for large files; bigger reads cut syscalls and let BLAKE3 hash chunks in parallel.</summary>
    public int LargeFileBufferSize { get; init; } = 4 * ByteSize.OneMB;

    public long LargeFileThreshold { get; init; } = ByteSize.OneGB;

    /// <summary>Chunks at least this large are hashed with BLAKE3's multi-threaded mode.</summary>
    public int Blake3ParallelThreshold { get; init; } = 512 * ByteSize.OneKB;

    public static HashEngineOptions Default { get; } = new();
}

/// <summary>
/// Computes file digests. The file is read once no matter how many algorithms are requested, and the next block is read
/// while the current one is being hashed so that disk and CPU work overlap.
/// </summary>
public sealed class HashEngine(HashEngineOptions? options = null)
{
    private readonly HashEngineOptions _options = options ?? HashEngineOptions.Default;

    public HashEngineOptions Options => _options;

    public async Task<HashResult> ComputeFileAsync(string path, HashType algorithm,
        Action<long>? onBytesRead = null, CancellationToken cancellationToken = default)
    {
        var results = await ComputeFileAsync(path, [algorithm], onBytesRead, cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    public async Task<HashResult[]> ComputeFileAsync(string path, IReadOnlyList<HashType> algorithms,
        Action<long>? onBytesRead = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        cancellationToken.ThrowIfCancellationRequested();

        // Allow other processes to keep the file open for writing or deletion: refusing to hash a file just because a
        // downloader or editor still has a handle produced most of the "file in use" errors in 1.x.
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            BufferSize = 0,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        var bufferSize = stream.Length >= _options.LargeFileThreshold ? _options.LargeFileBufferSize : _options.BufferSize;
        return await ComputeStreamAsync(stream, algorithms, bufferSize, onBytesRead, cancellationToken).ConfigureAwait(false);
    }

    public Task<HashResult[]> ComputeStreamAsync(Stream stream, IReadOnlyList<HashType> algorithms,
        Action<long>? onBytesRead = null, CancellationToken cancellationToken = default) =>
        ComputeStreamAsync(stream, algorithms, _options.BufferSize, onBytesRead, cancellationToken);

    private async Task<HashResult[]> ComputeStreamAsync(Stream stream, IReadOnlyList<HashType> algorithms, int bufferSize,
        Action<long>? onBytesRead, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(algorithms);
        if (algorithms.Count == 0) throw new ArgumentException("At least one algorithm is required.", nameof(algorithms));

        var functions = new HashFunction[algorithms.Count];
        var current = ArrayPool<byte>.Shared.Rent(bufferSize);
        var next = ArrayPool<byte>.Shared.Rent(bufferSize);
        ValueTask<int> pendingRead = default;
        var readInFlight = false;
        try
        {
            for (var i = 0; i < functions.Length; i++)
                functions[i] = HashFunction.Create(algorithms[i], _options.Blake3ParallelThreshold);

            pendingRead = stream.ReadAsync(current.AsMemory(0, bufferSize), cancellationToken);
            readInFlight = true;
            while (true)
            {
                var read = await pendingRead.ConfigureAwait(false);
                readInFlight = false;
                if (read == 0) break;

                // Start reading the next block before hashing this one.
                pendingRead = stream.ReadAsync(next.AsMemory(0, bufferSize), cancellationToken);
                readInFlight = true;

                Append(functions, current, read);
                onBytesRead?.Invoke(read);
                (current, next) = (next, current);
            }

            var results = new HashResult[functions.Length];
            for (var i = 0; i < functions.Length; i++)
                results[i] = new HashResult(algorithms[i], functions[i].GetHash());
            return results;
        }
        finally
        {
            if (readInFlight)
            {
                // Never return a buffer to the pool while a read may still be writing into it.
                try
                {
                    await pendingRead.ConfigureAwait(false);
                }
                catch
                {
                    // The original exception (if any) is already propagating.
                }
            }

            foreach (var function in functions) function?.Dispose();
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(next);
        }
    }

    private static void Append(HashFunction[] functions, byte[] buffer, int count)
    {
        if (functions.Length == 1)
        {
            functions[0].Append(buffer.AsSpan(0, count));
            return;
        }

        Parallel.For(0, functions.Length, i => functions[i].Append(buffer.AsSpan(0, count)));
    }
}
