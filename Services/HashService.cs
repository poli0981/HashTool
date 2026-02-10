using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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
    BLAKE3
}

public class HashService
{
    private const int DefaultBufferSize = 80 * AppConstants.OneKB;
    private const int LargeBufferSize = AppConstants.OneMB;

    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token)
    {
        int bufferSize = type == HashType.BLAKE3 ? DefaultBufferSize : LargeBufferSize;

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var hashBytes = type switch
            {
                HashType.MD5 => await MD5.HashDataAsync(stream, token),
                HashType.SHA1 => await SHA1.HashDataAsync(stream, token),
                HashType.SHA256 => await SHA256.HashDataAsync(stream, token),
                HashType.SHA384 => await SHA384.HashDataAsync(stream, token),
                HashType.SHA512 => await SHA512.HashDataAsync(stream, token),
                HashType.BLAKE3 => await ComputeBlake3Async(stream, token, bufferSize),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown HashType")
            };

            return Convert.ToHexString(hashBytes);
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

    private async Task<byte[]> ComputeBlake3Async(Stream stream, CancellationToken token, int bufferSize)
    {
        using var hasher = Hasher.New();
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), token)) > 0)
                hasher.Update(new ReadOnlySpan<byte>(buffer, 0, bytesRead));

            return hasher.Finalize().AsSpan().ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}