namespace HashTool.Core.Hashing;

/// <summary>
/// Supported hash algorithms. The numeric values are persisted in settings and must not change.
/// </summary>
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
