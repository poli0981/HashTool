using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class DeveloperViewModel : ObservableObject
{
    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);

    public DeveloperViewModel()
    {
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]") Localization = new LocalizationProxy(LocalizationService.Instance);
        };
    }

    public LoggerService Logger => LoggerService.Instance;

    // Binding in Logger.Logs
    public AvaloniaList<string> Logs => Logger.Logs;

    [RelayCommand]
    private void ClearLogs()
    {
        Logger.ClearLogs();
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        Logger.IsRecording = !Logger.IsRecording;
    }

    [RelayCommand]
    private void CopyLogs()
    {
        var text = string.Join("\n", Logs);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } window
            })
        {
            window.Clipboard?.SetTextAsync(text);
            Logger.Log("Logs copied to clipboard.");
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        // Open the log folder in file explorer
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "HashTool", "log");
        UrlHelper.OpenLocalFolder(path);
    }
}