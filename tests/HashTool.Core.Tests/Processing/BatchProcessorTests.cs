using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Hashing;
using HashTool.Core.Processing;
using Xunit;

namespace HashTool.Core.Tests.Processing;

public sealed class BatchProcessorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed class Recorder : IHashJobObserver<string>
    {
        public readonly ConcurrentDictionary<string, HashJobResult> Completed = new();
        public readonly ConcurrentBag<string> Started = [];
        public long Bytes;
        public Action<string>? OnStart;

        public void OnStarted(string item)
        {
            Started.Add(item);
            OnStart?.Invoke(item);
        }

        public void OnProgress(string item, long bytesRead) => Interlocked.Add(ref Bytes, bytesRead);

        public void OnCompleted(string item, HashJobResult result) => Assert.True(Completed.TryAdd(item, result), $"{item} completed twice");
    }

    private static HashJob Job(string path) =>
        new(path, File.Exists(path) ? new FileInfo(path).Length : 0, [HashType.SHA256, HashType.CRC32]);

    [Fact]
    public async Task HashesEveryFile_AndReportsFailures()
    {
        var files = Enumerable.Range(0, 20).Select(i => _temp.File($"f{i}.bin", new byte[i * 100])).ToList();
        var missing = Path.Combine(_temp.Path, "missing.bin");
        var recorder = new Recorder();

        await new BatchProcessor(new HashEngine(), new BatchOptions { MaxParallelFiles = 4 })
            .RunAsync([.. files, missing], Job, recorder, TestContext.Current.CancellationToken);

        Assert.Equal(21, recorder.Completed.Count);
        Assert.All(files, f => Assert.Equal(HashJobStatus.Succeeded, recorder.Completed[f].Status));
        Assert.Equal(2, recorder.Completed[files[3]].Results!.Count);
        Assert.Equal(HashJobStatus.Failed, recorder.Completed[missing].Status);
        Assert.IsType<FileNotFoundException>(recorder.Completed[missing].Error);
        Assert.Equal(files.Sum(f => new FileInfo(f).Length), recorder.Bytes);
    }

    [Fact]
    public async Task Cancellation_ReportsEveryItemOnce()
    {
        var files = Enumerable.Range(0, 50).Select(i => _temp.File($"c{i}.bin", new byte[256 * 1024])).ToList();
        using var cts = new CancellationTokenSource();
        var recorder = new Recorder { OnStart = _ => cts.Cancel() };

        await new BatchProcessor(new HashEngine(), new BatchOptions { SequentialDisk = true })
            .RunAsync(files, Job, recorder, cts.Token);

        Assert.Equal(files.Count, recorder.Completed.Count);
        Assert.Contains(recorder.Completed.Values, r => r.Status == HashJobStatus.Cancelled);
        Assert.True(recorder.Started.Count < files.Count);
    }

    [Fact]
    public async Task ItemCancellation_StopsOnlyThatFile()
    {
        var keep = _temp.File("keep.bin", new byte[1000]);
        var stop = _temp.File("stop.bin", new byte[1000]);
        using var itemCts = new CancellationTokenSource();
        await itemCts.CancelAsync();
        var recorder = new Recorder();

        await new BatchProcessor(new HashEngine()).RunAsync([keep, stop],
            p => Job(p) with { ItemCancellation = p == stop ? itemCts.Token : default }, recorder,
            TestContext.Current.CancellationToken);

        Assert.Equal(HashJobStatus.Succeeded, recorder.Completed[keep].Status);
        Assert.Equal(HashJobStatus.Cancelled, recorder.Completed[stop].Status);
    }

    [Fact]
    public async Task NullJobs_AreSkipped()
    {
        var file = _temp.File("x.bin", [1]);
        var recorder = new Recorder();

        await new BatchProcessor(new HashEngine()).RunAsync([file, "skip"], p => p == "skip" ? null : Job(p), recorder,
            TestContext.Current.CancellationToken);

        Assert.Equal([file], recorder.Completed.Keys);
    }

    [Fact]
    public void Parallelism_Defaults()
    {
        Assert.Equal(1, new BatchOptions { SequentialDisk = true, MaxParallelFiles = 8 }.EffectiveParallelism);
        Assert.Equal(8, new BatchOptions { MaxParallelFiles = 8 }.EffectiveParallelism);
        Assert.InRange(new BatchOptions().EffectiveParallelism, 1, 4);
    }
}
