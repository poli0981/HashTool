using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckHash.Services;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace CheckHash.ViewModels;

public partial class DeveloperViewModel : ObservableObject
{
    public LoggerService Logger => LoggerService.Instance;
    public LocalizationService Localization => LocalizationService.Instance;

    // Binding in Logger.Logs
    public ObservableCollection<string> Logs => Logger.Logs;

    public DeveloperViewModel()
    {
        Localization.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                OnPropertyChanged(nameof(Localization));
            }
        };
    }

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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            window.Clipboard?.SetTextAsync(text);
            Logger.Log("Logs copied to clipboard.");
        }
    }
    
    [RelayCommand]
    private void OpenLogFolder()
    {
        // Open the log folder in file explorer
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        var path = System.IO.Path.Combine(appData, "CheckHash", "log");
        UrlHelper.Open(path);
    }
}