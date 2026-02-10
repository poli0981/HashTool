using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CheckHash.Services;

public static class UrlHelper
{
    public static void Open(string url)
    {
        if (!IsSafeUrl(url)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(url);
                Process.Start(startInfo);
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    public static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Check for HTTP/HTTPS
        if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            if (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)
                return true;

        return false;
    }
    public static void OpenLocalFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (!Path.IsPathRooted(path) || !Directory.Exists(path)) return;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fullPath = Path.GetFullPath(path);
            var safePath = Path.GetFullPath(Path.Combine(appData, "HashTool"));
            if (!fullPath.Equals(safePath, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(safePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else
            {
                var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(path);
                Process.Start(startInfo);
            }
        }
        catch
        {
            // Ignore errors
        }
    }
}