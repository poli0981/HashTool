using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CheckHash.Services;
using System.Collections.ObjectModel;
using System.Reflection;

namespace CheckHash.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public LocalizationService Localization => LocalizationService.Instance;

    // Basic Info
    public string AppName => "Hash Tool";
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    public string AuthorName => "Poli0981"; // My name :D
    public string GitHubProfile => "https://github.com/poli0981";
    public string Copyright => $"© 2026 {AuthorName}. All rights reserved.";
    
    // Credits 3rd party libraries
    public ObservableCollection<LibraryItem> Libraries { get; } = new()
    {
        new("Avalonia UI version 11.3.11 ", "MIT License", "https://avaloniaui.net/"),
        new("CommunityToolkit.Mvvm Ver 8.2.1", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
        new("Material.Icons.Avalonia Ver 2.4.1", "MIT License", "https://github.com/AvaloniaUtils/Material.Icons.Avalonia"),
        new("Velopack version 0.0.1298", "MIT License", "https://velopack.io/"),
    };

    // Open URL in default browser of user
    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
            UrlHelper.Open(url);
    }
}

public record LibraryItem(string Name, string License, string Url);