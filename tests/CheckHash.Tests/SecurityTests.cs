using Xunit;
using CheckHash.ViewModels;
using CheckHash.Services;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System;

namespace CheckHash.Tests;

public class SecurityTests
{
    public SecurityTests()
    {
        // Disable UI logging to avoid Dispatcher issues since we are not in an Avalonia App
        LoggerService.Instance.IsRecording = false;
    }

    [Fact]
    public async Task Test_ReadHashFromFile_LargeFile_DoS_Prevention()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write 2MB of data (Hex characters so it might match the regex if read)
            var largeContent = new string('A', 2 * 1024 * 1024);
            await File.WriteAllTextAsync(tempFile, largeContent);

            var viewModel = new CheckHashViewModel();
            var methodInfo = typeof(CheckHashViewModel).GetMethod("ReadHashFromFile", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(methodInfo);

            // Act
            // Invoke the private method
            var task = (Task<string>)methodInfo!.Invoke(viewModel, new object[] { tempFile })!;
            var result = await task;

            // Assert
            // After fix, it should return empty string because file is too large (assuming 1MB limit)
            // Before fix, it would read the file and likely return a match (128 chars of 'A')
            Assert.Equal("", result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
