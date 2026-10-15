using System;
using System.IO;

namespace HashTool.Core.Files;

/// <summary>Why reading a file failed, independent of the platform's exception messages.</summary>
public enum FileErrorKind
{
    None,
    NotFound,
    AccessDenied,
    InUse,
    TooLarge,
    Io,
    Unknown
}

public static class FileErrors
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static FileErrorKind Classify(Exception? exception) => exception switch
    {
        null => FileErrorKind.None,
        FileNotFoundException or DirectoryNotFoundException => FileErrorKind.NotFound,
        UnauthorizedAccessException => FileErrorKind.AccessDenied,
        IOException io when IsSharingViolation(io) => FileErrorKind.InUse,
        IOException => FileErrorKind.Io,
        _ => FileErrorKind.Unknown
    };

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return OperatingSystem.IsWindows() && code is ErrorSharingViolation or ErrorLockViolation;
    }
}
