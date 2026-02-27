namespace CheckHash.Services;

public static class HashTypeExtensions
{
    public static bool IsInsecure(this HashType type)
    {
        return type is HashType.MD5 or HashType.SHA1;
    }
}