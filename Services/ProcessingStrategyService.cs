using System;
using System.Collections.Generic;
using CheckHash.Models;

namespace CheckHash.Services;

public class ProcessingStrategyService
{
    private const long OneGB = 1024L * 1024 * 1024;
    private const long HeavyFileThreshold = 5L * 1024 * 1024 * 1024; // 5GB

    private const int OneMB = 1024 * 1024;
    private const int TwoMB = 2 * 1024 * 1024;
    private const int FourMB = 4 * 1024 * 1024;
    private const int Blake3BufferSize = 80 * 1024; // 80KB

    public List<List<T>> GetProcessingBatches<T>(IEnumerable<T> items, Func<T, long> sizeSelector)
    {
        var result = new List<List<T>> { new(), new(), new() };

        foreach (var item in items)
        {
            var size = sizeSelector(item);
            if (size < OneGB)
            {
                result[0].Add(item);
            }
            else if (size < HeavyFileThreshold)
            {
                result[1].Add(item);
            }
            else
            {
                result[2].Add(item);
            }
        }
        return result;
    }
    public int GetBufferSize(long size, HashType algorithm)
    {
        if (algorithm == HashType.BLAKE3)
        {
            return Blake3BufferSize;
        }

        if (size < OneGB)
        {
            return OneMB;
        }

        if (size < HeavyFileThreshold)
        {
            return TwoMB;
        }

        return FourMB;
    }
}