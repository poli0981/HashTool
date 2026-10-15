using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace HashTool.Core.Hashing;

/// <summary>Incremental hash computation for one algorithm.</summary>
internal abstract class HashFunction : IDisposable
{
    public abstract void Append(ReadOnlySpan<byte> data);

    public abstract byte[] GetHash();

    public virtual void Dispose()
    {
    }

    public static HashFunction Create(HashType type, int parallelThreshold) => type switch
    {
        HashType.MD5 => new IncrementalHashFunction(HashAlgorithmName.MD5),
        HashType.SHA1 => new IncrementalHashFunction(HashAlgorithmName.SHA1),
        HashType.SHA256 => new IncrementalHashFunction(HashAlgorithmName.SHA256),
        HashType.SHA384 => new IncrementalHashFunction(HashAlgorithmName.SHA384),
        HashType.SHA512 => new IncrementalHashFunction(HashAlgorithmName.SHA512),
        HashType.BLAKE3 => new Blake3HashFunction(parallelThreshold),
        HashType.XxHash32 => new NonCryptographicHashFunction(new XxHash32()),
        HashType.XxHash64 => new NonCryptographicHashFunction(new XxHash64()),
        HashType.XxHash3 => new NonCryptographicHashFunction(new XxHash3()),
        HashType.XxHash128 => new NonCryptographicHashFunction(new XxHash128()),
        HashType.CRC32 => new Crc32HashFunction(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown hash algorithm.")
    };

    private sealed class IncrementalHashFunction(HashAlgorithmName name) : HashFunction
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(name);

        public override void Append(ReadOnlySpan<byte> data) => _hash.AppendData(data);

        public override byte[] GetHash() => _hash.GetHashAndReset();

        public override void Dispose() => _hash.Dispose();
    }

    /// <summary>xxHash digests are written big-endian, matching xxhsum's canonical form.</summary>
    private sealed class NonCryptographicHashFunction(NonCryptographicHashAlgorithm algorithm) : HashFunction
    {
        public override void Append(ReadOnlySpan<byte> data) => algorithm.Append(data);

        public override byte[] GetHash() => algorithm.GetHashAndReset();
    }

    /// <summary>
    /// <c>Crc32.GetCurrentHash()</c> emits little-endian bytes; every checksum tool (7-Zip, SFV, cksum -a crc32b)
    /// prints the CRC as a big-endian number, so convert it.
    /// </summary>
    private sealed class Crc32HashFunction : HashFunction
    {
        private readonly Crc32 _crc = new();

        public override void Append(ReadOnlySpan<byte> data) => _crc.Append(data);

        public override byte[] GetHash()
        {
            var digest = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(digest, _crc.GetCurrentHashAsUInt32());
            _crc.Reset();
            return digest;
        }
    }

    private sealed class Blake3HashFunction(int parallelThreshold) : HashFunction
    {
        private Blake3.Hasher _hasher = Blake3.Hasher.New();

        public override void Append(ReadOnlySpan<byte> data)
        {
            if (data.Length >= parallelThreshold) _hasher.UpdateWithJoin(data);
            else _hasher.Update(data);
        }

        public override byte[] GetHash()
        {
            var digest = new byte[32];
            _hasher.Finalize(digest);
            return digest;
        }

        public override void Dispose() => _hasher.Dispose();
    }
}
