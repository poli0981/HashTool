namespace CheckHash.Models;

public enum FileSizeFilter
{
    All,
    Small, // < 1MB
    Medium, // 1MB - 100MB
    Large, // 100MB - 1GB
    ExtraLarge // > 1GB
}