using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.App.Views;

namespace HashTool.App.Infrastructure;

/// <summary>
/// <c>CheckHash --smoke-test [--smoke-out result.json] [--expect-version 1.2.0]</c>: starts the app headless, loads every
/// page and language, hashes and verifies a file, and reports the Velopack install state. Used by CI and the release
/// upgrade gate on published (single-file) builds.
/// </summary>
public static class SmokeTest
{
    private const string Sha256Of123456789 = "15E2B0D3C33891EBB0F1EF609EC419420C20E320CE94C65FBC8C3312448EB225";

    public static int Run(AppCommandLine commandLine)
    {
        // Never touch the user's real settings unless the caller points us at a data folder on purpose.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)))
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, Directory.CreateTempSubdirectory("hashtool-smoke-").FullName);

        var report = new SmokeReport();
        try
        {
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont().SetupWithoutStarting();
            using var done = new CancellationTokenSource();
            var checks = RunChecksAsync(commandLine, report);
            _ = checks.ContinueWith(_ => done.Cancel(), TaskScheduler.Default);
            Dispatcher.UIThread.MainLoop(done.Token);
            checks.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.ToString());
        }
        finally
        {
            AppHost.Shutdown();
        }

        report.Ok = report.Errors.Count == 0;
        var json = JsonSerializer.Serialize(report, SmokeJsonContext.Default.SmokeReport);
        if (commandLine.SmokeOutput is { } output) File.WriteAllText(output, json);
        Console.Out.WriteLine(json);
        return report.Ok ? 0 : 1;
    }

    private static async Task RunChecksAsync(AppCommandLine commandLine, SmokeReport report)
    {
        await Task.Yield();
        var updates = AppHost.Get<UpdateService>();
        report.Version = updates.CurrentVersion;
        report.Installed = updates.IsInstalled;
        report.Portable = updates.IsPortable;
        report.DataDirectory = AppPaths.DataDirectory;
        if (commandLine.ExpectedVersion is { } expected && !string.Equals(updates.CurrentVersion, expected, StringComparison.Ordinal))
            report.Errors.Add($"Expected version {expected}, found {updates.CurrentVersion}");

        // Every page renders with its view model.
        var main = AppHost.Get<MainWindowViewModel>();
        var window = new MainWindow { DataContext = main };
        window.Show();
        foreach (var page in new[] { "Check", "Settings", "Update", "About", "Developer", "Create" })
        {
            main.NavigateCommand.Execute(page);
            Dispatcher.UIThread.RunJobs();
        }

        // Every language has its satellite resources and core strings.
        var localization = AppHost.Get<LocalizationService>();
        foreach (var language in localization.Languages)
        {
            localization.SelectedLanguage = language;
            Dispatcher.UIThread.RunJobs();
            foreach (var key in new[] { "AppTitle", "Menu_Create", "Status_Valid", "Btn_CheckAll" })
            {
                if (localization[key].StartsWith('[')) report.Errors.Add($"{language.Code}: missing {key}");
            }

            if (!localization.HasResourcesFor(language.Code) && !language.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                report.Errors.Add($"{language.Code}: satellite assembly not found");
        }

        report.Languages = localization.Languages.Count;
        localization.SelectedLanguage = localization.AvailableLanguages[0];

        // Hash and verify a real file.
        var folder = Directory.CreateTempSubdirectory("hashtool-smoke-files-");
        try
        {
            var file = Path.Combine(folder.FullName, "check.txt");
            await File.WriteAllTextAsync(file, "123456789");
            main.Create.SelectedAlgorithm = Core.Hashing.HashType.SHA256;
            await main.Create.AddPathsAsync([file]);
            await main.Create.RunAllCommand.ExecuteAsync(null);
            var created = main.Create.Items.Single();
            if (!string.Equals(created.ResultHash, Sha256Of123456789, StringComparison.OrdinalIgnoreCase))
                report.Errors.Add($"Create: unexpected hash {created.ResultHash} ({created.Status} {created.StatusDetail})");

            await File.WriteAllTextAsync(file + ".sha256", $"{Sha256Of123456789.ToLowerInvariant()}  check.txt\n");
            await main.Check.AddPathsAsync([file + ".sha256"]);
            await main.Check.RunAllCommand.ExecuteAsync(null);
            var verified = main.Check.Items.Single();
            if (verified.IsMatch != true) report.Errors.Add($"Verify: {verified.Status} {verified.StatusDetail}");
        }
        finally
        {
            window.Close();
            folder.Delete(recursive: true);
        }
    }

    internal sealed class SmokeReport
    {
        public bool Ok { get; set; }
        public string? Version { get; set; }
        public bool Installed { get; set; }
        public bool Portable { get; set; }
        public int Languages { get; set; }
        public string? DataDirectory { get; set; }
        public string Culture { get; set; } = CultureInfo.CurrentUICulture.Name;
        public List<string> Errors { get; } = [];
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SmokeTest.SmokeReport))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
