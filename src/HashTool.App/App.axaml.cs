using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HashTool.App.Infrastructure;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.App.ViewModels;
using HashTool.App.Views;
using Microsoft.Extensions.Logging;

namespace HashTool.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppHost.Initialize();
        var logger = AppHost.Get<ILoggerFactory>().CreateLogger<App>();

        // macOS delivers "Open With" files through activation events; subscribe before anything awaits.
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs files)
                {
                    var paths = files.Files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                    AppHost.Get<ActivationService>().Enqueue(new ActivationRequest(ActivationVerb.Auto, paths));
                }
            };
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true;
            var localization = AppHost.Get<LocalizationService>();
            _ = AppHost.Get<IDialogService>().ShowErrorAsync(localization["Msg_Error"],
                localization.Format("Msg_UnexpectedError", e.Exception.Message), e.Exception.ToString());
        };

        AppHost.Get<ThemeService>().Attach(this);
        var settingsPage = AppHost.Get<SettingsViewModel>();
        settingsPage.Apply();
        _ = AppHost.Get<FontService>().LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(settingsPage.Apply),
            System.Threading.Tasks.TaskScheduler.Default);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = AppHost.Get<MainWindowViewModel>();
            var window = new MainWindow { DataContext = main };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => AppHost.Get<SettingsService>().Flush();
            EventHandler shutdown = (_, _) => desktop.Shutdown();
            main.Create.RequestShutdown += shutdown;
            main.Check.RequestShutdown += shutdown;
            settingsPage.RequestShutdown += shutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
