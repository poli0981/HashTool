using System;
using System.Collections.Generic;
using System.Linq;

namespace CheckHash.Services;

public record ProcessingOptions(int MaxDegreeOfParallelism, int? BufferSize);

public class ProcessingStrategyService
{
    private const long HeavyFileThreshold = 5L * 1024 * 1024 * 1024; // 5GB
    private const int OneMB = 1024 * 1024;
    private const int FourMB = 4 * 1024 * 1024;
    private const int TwoMB = 2 * 1024 * 1024;

    public ProcessingOptions GetProcessingOptions(IEnumerable<long> fileSizes)
    {
        var sizes = fileSizes as IReadOnlyList<long> ?? fileSizes.ToList();
        var count = sizes.Count;

        if (count == 0)
        {
            return new ProcessingOptions(Environment.ProcessorCount, null);
        }

        var heavyCount = sizes.Count(size => size > HeavyFileThreshold);
        var lightCount = count - heavyCount;

        if (heavyCount == count)
        {
            var concurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 2));
            return new ProcessingOptions(concurrency, FourMB);
        }

        if (lightCount == count)
        {
            return new ProcessingOptions(Environment.ProcessorCount, OneMB);
        }

        var mixedConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
        return new ProcessingOptions(mixedConcurrency, TwoMB);
    }
}