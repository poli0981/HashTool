using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Services;

namespace HashTool.App.ViewModels;

public sealed record LibraryItem(string Name, string Version, string License, string Url);

public sealed record DocumentItem(string LabelKey, string Url) : ILabeled;

/// <summary>The About page. Library versions are read from the loaded assemblies, so they never go stale.</summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private const string DocsBase = "https://github.com/poli0981/HashTool/blob/main";

    private readonly ILauncherService _launcher;
    private readonly LocalizationService _localizationService;

    [ObservableProperty] private LocalizationProxy _localization;

    public AboutViewModel(UpdateService updates, LocalizationService localization, ILauncherService launcher)
    {
        _launcher = launcher;
        _localizationService = localization;
        _localization = new LocalizationProxy(localization);
        Version = updates.CurrentVersion;
        localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(TechnologyText));
        };
    }

    public string AppName => "Hash Tool";

    public string Version { get; }

    public string VersionText => _localizationService.Format("About_Version", Version);

    public string TechnologyText => _localizationService.Format("About_TechContent", RuntimeInformation.FrameworkDescription);

    public string AuthorName => "poli0981";

    public string GitHubProfile => "https://github.com/poli0981";

    public string Copyright => $"© 2026–{Math.Max(2026, DateTime.Now.Year)} {AuthorName}";

    public IReadOnlyList<DocumentItem> Documents { get; } =
    [
        new("Doc_PrivacyPolicy", $"{DocsBase}/docs/PrivacyPolicy.md"),
        new("Doc_ToS", $"{DocsBase}/docs/ToS.md"),
        new("Doc_EULA", $"{DocsBase}/docs/EULA.md"),
        new("Doc_Disclaimer", $"{DocsBase}/docs/DISCLAIMER.md"),
        new("Doc_Readme", $"{DocsBase}/README.md"),
        new("Doc_Changelog", $"{DocsBase}/CHANGELOG.md"),
        new("Doc_Credit", $"{DocsBase}/ACKNOWLEDGEMENTS.md")
    ];

    public IReadOnlyList<LibraryItem> Libraries { get; } =
    [
        Library("Avalonia UI", typeof(Avalonia.Application), "MIT", "https://avaloniaui.net/"),
        Library("Inter font", typeof(Avalonia.Fonts.Inter.InterFontCollection), "SIL OFL 1.1", "https://rsms.me/inter/"),
        Library("Blake3.NET", typeof(Blake3.Hasher), "BSD-2-Clause", "https://github.com/xoofx/Blake3.NET"),
        Library("CommunityToolkit.Mvvm", typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject), "MIT", "https://github.com/CommunityToolkit/dotnet"),
        Library("Material.Icons.Avalonia", typeof(Material.Icons.Avalonia.MaterialIcon), "MIT", "https://github.com/SKProCH/Material.Icons"),
        Library("Microsoft.Extensions.*", typeof(Microsoft.Extensions.Logging.ILogger), "MIT", "https://github.com/dotnet/runtime"),
        Library("System.IO.Hashing", typeof(System.IO.Hashing.XxHash3), "MIT", "https://github.com/dotnet/runtime"),
        Library("Velopack", typeof(Velopack.UpdateManager), "MIT", "https://github.com/velopack/velopack")
    ];

    [RelayCommand]
    private Task OpenUrlAsync(string? url) => string.IsNullOrEmpty(url) ? Task.CompletedTask : _launcher.OpenUrlAsync(url);

    private static LibraryItem Library(string name, Type type, string license, string url)
    {
        var assembly = type.Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "";
        var plus = version.IndexOf('+');
        return new LibraryItem(name, plus > 0 ? version[..plus] : version, license, url);
    }
}
