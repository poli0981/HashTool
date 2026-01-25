using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CheckHash.Services;

public enum HashType { MD5, SHA1, SHA256, SHA384, SHA512 }

public class HashService
{
    private const int BufferSize = 1024 * 1024; 

    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File corrupted or deleted.");
        }

        if (IsFileLocked(filePath))
        {
            throw new IOException("File is being used by another process.");
        }

        try
        {
            using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("Access denied. Run as Administrator.");
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException("File corrupted or deleted.");
        }

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
             throw new FileNotFoundException("File corrupted or deleted during process.");
        }
        catch (IOException ex)
        {
             throw new IOException($"IO Error: {ex.Message}", ex);
        }
    }

    private bool IsFileLocked(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                stream.Close();
            }
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            // Ignore other exceptions in check
        }

        return false;
    }
}
