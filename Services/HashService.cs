using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Hashing;
using Blake3;
using CheckHash.Models;

namespace CheckHash.Services;

public enum HashType
{
    MD5,
    SHA1,
    SHA256,
    SHA384,
    SHA512,
    BLAKE3,
    XxHash32,
    XxHash64,
    XxHash3,
    XxHash128,
    CRC32
}

public class HashService
{
    private const int DefaultBufferSize = 80 * AppConstants.OneKB;
    private const int LargeBufferSize = AppConstants.OneMB;

    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token, int? bufferSize = null, Action<long>? progressCallback = null)
    {
        int actualBufferSize = bufferSize ?? (type == HashType.BLAKE3 ? DefaultBufferSize : LargeBufferSize);

        try
        {
            Stream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                actualBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (progressCallback != null)
            {
                stream = new ProgressStream(stream, progressCallback);
            }

            await using (stream)
            {
                byte[] hashBytes;

                switch (type)
                {
                    case HashType.MD5:
                    case HashType.SHA1:
                    case HashType.SHA256:
                    case HashType.SHA384:
                    case HashType.SHA512:
                        hashBytes = await ComputeIncrementalHashAsync(stream, type, token, actualBufferSize);
                        break;
                    case HashType.BLAKE3:
                        hashBytes = await ComputeBlake3Async(stream, token, actualBufferSize);
                        break;
                    case HashType.XxHash32:
                    case HashType.XxHash64:
                    case HashType.XxHash3:
                    case HashType.XxHash128:
                    case HashType.CRC32:
                        hashBytes = await ComputeNonCryptoHashAsync(stream, type, token, actualBufferSize);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown HashType");
                }

                return Convert.ToHexString(hashBytes);
            }
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException("File corrupted or deleted.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("Access denied. Run as Administrator.");
        }
        catch (IOException ex)
        {
            // Check if it's a sharing violation (HRESULT 0x80070020)
            var hr = Marshal.GetHRForException(ex);
            if ((hr & 0xFFFF) == 32) throw new IOException("File is being used by another process.");

            throw new IOException($"IO Error: {ex.Message}", ex);
        }
    }

    private async Task<byte[]> ComputeIncrementalHashAsync(Stream stream, HashType type, CancellationToken token, int bufferSize)
    {
        var algName = type switch
        {
            HashType.MD5 => HashAlgorithmName.MD5,
            HashType.SHA1 => HashAlgorithmName.SHA1,
            HashType.SHA256 => HashAlgorithmName.SHA256,
            HashType.SHA384 => HashAlgorithmName.SHA384,
            HashType.SHA512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException("Invalid hash type for IncrementalHash", nameof(type))
        };

        using var hasher = IncrementalHash.CreateHash(algName);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                hasher.AppendData(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
            }
            return hasher.GetCurrentHash();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<byte[]> ComputeBlake3Async(Stream stream, CancellationToken token, int bufferSize)
    {
        using var hasher = Hasher.New();
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                hasher.Update(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
            }
            return hasher.Finalize().AsSpan().ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<byte[]> ComputeNonCryptoHashAsync(Stream stream, HashType type, CancellationToken token, int bufferSize)
    {
        NonCryptographicHashAlgorithm hasher = type switch
        {
            HashType.XxHash32 => new XxHash32(),
            HashType.XxHash64 => new XxHash64(),
            HashType.XxHash3 => new XxHash3(),
            HashType.XxHash128 => new XxHash128(),
            HashType.CRC32 => new Crc32(),
            _ => throw new ArgumentException("Invalid hash type for NonCryptographicHashAlgorithm", nameof(type))
        };

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                hasher.Append(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
            }

            // Allocate exact size for result
            var result = new byte[hasher.HashLengthInBytes];
            hasher.GetCurrentHash(result);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}