using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HashTool.App.Services;
using HashTool.App.ViewModels;
using HashTool.App.Views;

namespace HashTool.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Setup Global Exception Handling
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LoggerService.Instance.Log($"Unhandled Exception: {ex?.Message}\nStackTrace: {ex?.StackTrace}",
                LogLevel.Error);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LoggerService.Instance.Log($"Unobserved Task Exception: {args.Exception.Message}", LogLevel.Error);
            args.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

}
