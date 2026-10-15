using System;
using HashTool.App.Infrastructure;
using HashTool.App.Infrastructure.Logging;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.App.ViewModels;
using HashTool.Core.Configuration;
using HashTool.Core.Hashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HashTool.App;

/// <summary>Composition root: builds the service provider and owns logging for the process lifetime.</summary>
public static class AppHost
{
    private static ServiceProvider? _provider;

    public static IServiceProvider Services => _provider ?? throw new InvalidOperationException("AppHost.Initialize was not called.");

    public static bool IsInitialized => _provider is not null;

    public static FileLoggerProvider? FileLogger { get; private set; }

    public static void Initialize()
    {
        if (_provider is not null) return;

        var logLevel = new LogLevelSwitch();
        var sanitizer = PathSanitizer.Shared;
        FileLogger = new FileLoggerProvider(AppPaths.LogDirectory, () => logLevel.Minimum, sanitizer);
        var sink = new LogSink(sanitizer);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Debug).AddProvider(FileLogger).AddProvider(sink));
        services.AddSingleton(logLevel);
        services.AddSingleton(sink);
        services.AddSingleton(sp => new SettingsStore(AppPaths.SettingsDirectory, sp.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<FontService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ActivationService>();
        services.AddSingleton<MainWindowAccessor>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ILauncherService, LauncherService>();
        services.AddSingleton(_ => new HashEngine());
        services.AddSingleton<FileListServices>();
        services.AddSingleton<CreateHashViewModel>();
        services.AddSingleton<CheckHashViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<DeveloperViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        _provider = services.BuildServiceProvider();

        var logger = _provider.GetRequiredService<ILoggerFactory>().CreateLogger("HashTool");
        CrashReporter.Install(FileLogger, logger);
        logger.LogInformation("Hash Tool {Version} starting ({Os}, {Runtime})", CrashReporter.Version,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    }

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Saves settings and flushes logs. Safe to call more than once.</summary>
    public static void Shutdown()
    {
        if (_provider is null) return;
        try
        {
            _provider.GetService<SettingsService>()?.Flush();
            _provider.GetRequiredService<ILoggerFactory>().CreateLogger("HashTool").LogInformation("Hash Tool exiting");
        }
        finally
        {
            _provider.Dispose();
            _provider = null;
            FileLogger?.Dispose();
        }
    }
}
