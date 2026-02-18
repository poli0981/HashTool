using System.Collections.ObjectModel;
using System.Reflection;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);

    public AboutViewModel()
    {
        UpdateDocuments();
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                Localization = new LocalizationProxy(LocalizationService.Instance);
                UpdateDocuments();
            }
        };
    }

    private void UpdateDocuments()
    {
        Documents.Clear();
        const string baseUrl = "https://github.com/poli0981/HashTool/blob/main";

        Documents.Add(new DocumentItem(Localization["Doc_PrivacyPolicy"], $"{baseUrl}/docs/PrivacyPolicy.md"));
        Documents.Add(new DocumentItem(Localization["Doc_ToS"], $"{baseUrl}/docs/ToS.md"));
        Documents.Add(new DocumentItem(Localization["Doc_EULA"], $"{baseUrl}/docs/EULA.md"));
        Documents.Add(new DocumentItem(Localization["Doc_Disclaimer"], $"{baseUrl}/docs/DISCLAIMER.md"));
        Documents.Add(new DocumentItem(Localization["Doc_Readme"], $"{baseUrl}/README.md"));
        Documents.Add(new DocumentItem(Localization["Doc_Changelog"], $"{baseUrl}/CHANGELOG.md"));
        Documents.Add(new DocumentItem(Localization["Doc_Credit"], $"{baseUrl}/ACKNOWLEDGEMENTS.md"));
    }

    // Basic Info
    public string AppName => "Hash Tool";
    public string Version => UpdateService.Instance.CurrentVersion;
    public string AuthorName => "Poli0981"; // My name :D
    public string GitHubProfile => "https://github.com/poli0981";
    public string Copyright => $"© 2026 {AuthorName}. All rights reserved.";

    // Documents
    public ObservableCollection<DocumentItem> Documents { get; } = new();

    // Credits 3rd party libraries
    public ObservableCollection<LibraryItem> Libraries { get; } = new()
    {
        new LibraryItem("Avalonia UI version 11.3.11", "MIT License", "https://avaloniaui.net/"),
        new LibraryItem("Avalonia.Controls.ColorPicker version 11.3.11", "MIT License",
            "https://github.com/AvaloniaUI/Avalonia"),
        new LibraryItem("Avalonia.Controls.DataGrid version 11.3.11", "MIT License",
            "https://github.com/AvaloniaUI/Avalonia"),
        new LibraryItem("Avalonia.Desktop version 11.3.11", "MIT License", "https://github.com/AvaloniaUI/Avalonia"),
        new LibraryItem("Avalonia.Fonts.Inter version 11.3.11", "MIT License / SIL OFL 1.1",
            "https://github.com/AvaloniaUI/Avalonia"),
        new LibraryItem("Avalonia.Themes.Fluent version 11.3.11", "MIT License",
            "https://github.com/AvaloniaUI/Avalonia"),
        new LibraryItem("Blake3.NET version 2.2.0",
            "Copyright (c) Alexandre Mutel. All rights reserved.\nLicensed under the BSD-" +
            "2-Clause License.", "https://github.com/xoofx/Blake3.NET"),
        new LibraryItem("CommunityToolkit.Mvvm Ver 8.2.1", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
        new LibraryItem("Material.Icons.Avalonia Ver 2.4.1", "MIT License",
            "https://github.com/AvaloniaUtils/Material.Icons.Avalonia"),
        new LibraryItem("Velopack version 0.0.1298", "MIT License", "https://github.com/velopack/velopack")
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
public record DocumentItem(string Name, string Url);