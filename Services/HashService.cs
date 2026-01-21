using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CheckHash.Services;

public enum HashType { MD5, SHA1, SHA256, SHA384, SHA512 }

public class HashService
{
    // Tính toán Hash bất đồng bộ
    public async Task<string> ComputeHashAsync(string filePath, HashType type, CancellationToken token)
    {
        // Kiểm tra file tồn tại trước khi xử lý
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Không tìm thấy file: {filePath}", filePath);
        }

        // Kiểm tra quyền truy cập file
        try
        {
            using var stream = File.OpenRead(filePath);
            
            byte[] hashBytes = type switch
            {
                HashType.MD5 => await MD5.HashDataAsync(stream, token),
                HashType.SHA1 => await SHA1.HashDataAsync(stream, token),
                HashType.SHA256 => await SHA256.HashDataAsync(stream, token),
                HashType.SHA384 => await SHA384.HashDataAsync(stream, token),
                HashType.SHA512 => await SHA512.HashDataAsync(stream, token),
                _ => throw new System.NotImplementedException()
            };

            return Convert.ToHexString(hashBytes); // .NET hiện đại dùng cái này nhanh hơn BitConverter
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"Không có quyền truy cập file: {filePath}", ex);
        }
        catch (IOException ex)
        {
            throw new IOException($"Lỗi đọc file (có thể đang được sử dụng bởi ứng dụng khác): {filePath}", ex);
        }
    }
}