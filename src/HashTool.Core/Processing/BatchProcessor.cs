using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Hashing;

namespace HashTool.Core.Processing;

public sealed record BatchOptions
{
    /// <summary>Files hashed at the same time; 0 picks a value from the processor count.</summary>
    public int MaxParallelFiles { get; init; }

    /// <summary>Hash one file at a time (spinning disks slow down badly when several files are read at once).</summary>
    public bool SequentialDisk { get; init; }

    /// <summary>Files at least this large are hashed one at a time in their own lane.</summary>
    public long LargeFileThreshold { get; init; } = ByteSize.OneGB;

    /// <summary>Give up on a file after this long; <c>null</c> waits forever.</summary>
    public TimeSpan? PerFileTimeout { get; init; }

    public int EffectiveParallelism =>
        SequentialDisk ? 1 : MaxParallelFiles > 0 ? MaxParallelFiles : Math.Clamp(Environment.ProcessorCount, 1, 4);
}

/// <summary>A file to hash.</summary>
/// <param name="ItemCancellation">Cancels only this file (e.g. the row's stop button).</param>
public sealed record HashJob(string Path, long Size, IReadOnlyList<HashType> Algorithms, CancellationToken ItemCancellation = default);

public enum HashJobStatus
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}

public sealed record HashJobResult(HashJobStatus Status, IReadOnlyList<HashResult>? Results, Exception? Error, TimeSpan Elapsed);

/// <summary>Receives progress from <see cref="BatchProcessor"/>. Called from worker threads.</summary>
public interface IHashJobObserver<in TItem>
{
    void OnStarted(TItem item);

    void OnProgress(TItem item, long bytesRead);

    void OnCompleted(TItem item, HashJobResult result);
}

/// <summary>
/// Hashes many files: small files run in parallel, large files one at a time in a separate lane so a 50 GB image does not
/// hold back thousands of small files. Every item gets exactly one <see cref="IHashJobObserver{TItem}.OnCompleted"/>
/// call, including items that never started because the batch was cancelled.
/// </summary>
public sealed class BatchProcessor(HashEngine engine, BatchOptions? options = null)
{
    private readonly BatchOptions _options = options ?? new BatchOptions();

    public async Task RunAsync<TItem>(IReadOnlyList<TItem> items, Func<TItem, HashJob?> createJob,
        IHashJobObserver<TItem> observer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(createJob);
        ArgumentNullException.ThrowIfNull(observer);

        var small = new List<Work<TItem>>();
        var large = new List<Work<TItem>>();
        foreach (var item in items)
        {
            var job = createJob(item);
            if (job is null) continue;
            var work = new Work<TItem>(item, job);
            if (!_options.SequentialDisk && job.Size >= _options.LargeFileThreshold) large.Add(work);
            else small.Add(work);
        }

        try
        {
            await Task.WhenAll(
                RunLaneAsync(small, _options.EffectiveParallelism, observer, cancellationToken),
                RunLaneAsync(large, 1, observer, cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Reported per item below.
        }

        foreach (var work in small) ReportIfNotStarted(work, observer);
        foreach (var work in large) ReportIfNotStarted(work, observer);
    }

    private async Task RunLaneAsync<TItem>(List<Work<TItem>> lane, int parallelism, IHashJobObserver<TItem> observer,
        CancellationToken cancellationToken)
    {
        if (lane.Count == 0) return;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(lane, parallelOptions,
            async (work, _) => await ProcessAsync(work, observer, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task ProcessAsync<TItem>(Work<TItem> work, IHashJobObserver<TItem> observer, CancellationToken batchToken)
    {
        if (Interlocked.Exchange(ref work.Started, 1) == 1) return;
        var job = work.Job;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(batchToken, job.ItemCancellation);
        if (_options.PerFileTimeout is { } timeout) cts.CancelAfter(timeout);

        observer.OnStarted(work.Item);
        var stopwatch = Stopwatch.StartNew();
        HashJobResult result;
        try
        {
            var hashes = await engine.ComputeFileAsync(job.Path, job.Algorithms,
                read => observer.OnProgress(work.Item, read), cts.Token).ConfigureAwait(false);
            result = new HashJobResult(HashJobStatus.Succeeded, hashes, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            var timedOut = !batchToken.IsCancellationRequested && !job.ItemCancellation.IsCancellationRequested;
            result = new HashJobResult(timedOut ? HashJobStatus.TimedOut : HashJobStatus.Cancelled, null, null, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            result = new HashJobResult(HashJobStatus.Failed, null, ex, stopwatch.Elapsed);
        }

        observer.OnCompleted(work.Item, result);
    }

    private static void ReportIfNotStarted<TItem>(Work<TItem> work, IHashJobObserver<TItem> observer)
    {
        if (Interlocked.Exchange(ref work.Started, 1) == 0)
            observer.OnCompleted(work.Item, new HashJobResult(HashJobStatus.Cancelled, null, null, TimeSpan.Zero));
    }

    private sealed class Work<TItem>(TItem item, HashJob job)
    {
        public readonly TItem Item = item;
        public readonly HashJob Job = job;
        public int Started;
    }
}
