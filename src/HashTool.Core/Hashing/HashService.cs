using System;
using System.Threading;
using System.Threading.Tasks;

namespace HashTool.Core.Hashing;

/// <summary>Single-algorithm convenience wrapper over <see cref="HashEngine"/> that returns upper-case hex.</summary>
public class HashService(HashEngine? engine = null)
{
    private readonly HashEngine _engine = engine ?? new HashEngine();

    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token,
        int? bufferSize = null, Action<long>? progressCallback = null)
    {
        var result = await _engine.ComputeFileAsync(filePath, type, progressCallback, token).ConfigureAwait(false);
        return result.ToHex();
    }
}
