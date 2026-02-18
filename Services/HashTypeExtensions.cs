namespace CheckHash.Services;

public static class HashTypeExtensions
{
    public static bool IsInsecure(this HashType type)
    {
        return type == HashType.MD5 || type == HashType.SHA1;
    }
}