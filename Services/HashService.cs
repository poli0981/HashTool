using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CheckHash.Services;

public enum HashType { MD5, SHA1, SHA256, SHA384, SHA512 }

public class HashService
{
    private const int BufferSize = 81920;

    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token)
    {
        try
        {
            using var stream = new FileStream(
                filePath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                bufferSize: BufferSize, 
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            byte[] hashBytes = type switch
            {
                HashType.MD5 => await MD5.HashDataAsync(stream, token),
                HashType.SHA1 => await SHA1.HashDataAsync(stream, token),
                HashType.SHA256 => await SHA256.HashDataAsync(stream, token),
                HashType.SHA384 => await SHA384.HashDataAsync(stream, token),
                HashType.SHA512 => await SHA512.HashDataAsync(stream, token),
                _ => throw new NotImplementedException()
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
             int hr = Marshal.GetHRForException(ex);
             if ((hr & 0xFFFF) == 32)
             {
                 throw new IOException("File is being used by another process.");
             }

             throw new IOException($"IO Error: {ex.Message}", ex);
        }
    }
}
