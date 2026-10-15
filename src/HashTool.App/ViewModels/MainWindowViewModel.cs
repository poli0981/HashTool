using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashTool.App.Infrastructure;
using HashTool.App.Services;
using HashTool.App.Services.Theme;
using HashTool.Core.Checksums;
using HashTool.Core.Configuration;

namespace HashTool.App.ViewModels;

/// <summary>Navigation, window-level commands and notices.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ActivationService _activation;

    [ObservableProperty] private ObservableObject _currentPage;
    [ObservableProperty] private LocalizationProxy _localization;
    [ObservableProperty] private bool _isPaneOpen = true;
    [ObservableProperty] private string? _noticeText;

    public MainWindowViewModel(CreateHashViewModel create, CheckHashViewModel check, SettingsViewModel settingsPage,
        UpdateViewModel update, AboutViewModel about, DeveloperViewModel developer, SettingsService settings,
        LocalizationService localization, FontService fonts, ThemeService theme, ActivationService activation)
    {
        Create = create;
        Check = check;
        SettingsPage = settingsPage;
        Update = update;
        About = about;
        Developer = developer;
        _settings = settings;
        _activation = activation;
        Loc = localization;
        Fonts = fonts;
        Theme = theme;
        _currentPage = create;
        _localization = new LocalizationProxy(localization);

        localization.LanguageChanged += (_, _) =>
        {
            Localization = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(UpdateBannerText));
            settingsPage.RefreshShellIntegration();
        };
        create.PropertyChanged += OnPagePropertyChanged;
        check.PropertyChanged += OnPagePropertyChanged;
        settings.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsDeveloperModeEnabled));
            OnPropertyChanged(nameof(IsHashMaskingEnabled));
        };
        update.UpdateFound += (_, _) => OnPropertyChanged(nameof(UpdateBannerText));
        update.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.State)) OnPropertyChanged(nameof(UpdateBannerText));
        };

        if (settings.LoadResult == SettingsLoadResult.RecoveredFromCorruption) _noticeText = localization["Msg_SettingsRecovered"];
        else if (Elevation.IsElevated && OperatingSystem.IsWindows()) _noticeText = localization["Msg_ElevatedDragDrop"];
    }

    public CreateHashViewModel Create { get; }
    public CheckHashViewModel Check { get; }
    public SettingsViewModel SettingsPage { get; }
    public UpdateViewModel Update { get; }
    public AboutViewModel About { get; }
    public DeveloperViewModel Developer { get; }
    public LocalizationService Loc { get; }
    public FontService Fonts { get; }
    public ThemeService Theme { get; }

    public AppSettings Settings => _settings.Current;

    public bool IsBusy => Create.IsBusy || Check.IsBusy || Create.IsImporting || Check.IsImporting;

    public bool IsIdle => !IsBusy;

    public bool IsDeveloperModeEnabled => _settings.Current.IsDeveloperModeEnabled;

    public bool IsHashMaskingEnabled => _settings.Current.IsHashMaskingEnabled;

    /// <summary>Text of the "new version available" banner, or null when there is nothing to show.</summary>
    public string? UpdateBannerText => Update.State == UpdateState.Available && CurrentPage != Update
        ? Loc.Format("Msg_UpdateAvailableBanner", Update.AvailableVersion)
        : null;

    private FileListViewModelBase? CurrentList => CurrentPage as FileListViewModelBase;

    /// <summary>Name of the visible page, used to highlight the navigation button.</summary>
    public string CurrentPageName => CurrentPage switch
    {
        CheckHashViewModel => "Check",
        SettingsViewModel => "Settings",
        UpdateViewModel => "Update",
        AboutViewModel => "About",
        DeveloperViewModel => "Developer",
        _ => "Create"
    };

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(CurrentPageName));
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileListViewModelBase.IsBusy) or nameof(FileListViewModelBase.IsImporting))
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>Called once the window is shown.</summary>
    public async Task OnOpenedAsync()
    {
        _activation.Attach(HandleActivationAsync);
        await Update.CheckInBackgroundIfDueAsync();
    }

    /// <summary>Files from the command line, Explorer, a second launch or macOS "Open With".</summary>
    public async Task HandleActivationAsync(ActivationRequest request)
    {
        var verify = request.Verb switch
        {
            ActivationVerb.Verify => true,
            ActivationVerb.Create => false,
            _ => request.Paths.All(p => ChecksumFileNames.IsChecksumFile(p, out _))
        };
        FileListViewModelBase target = verify ? Check : Create;
        CurrentPage = target;
        ActivateWindowRequested?.Invoke(this, EventArgs.Empty);
        // Files handed over while that page is hashing or importing are added once it is free again.
        while (!target.CanModify) await Task.Delay(250);
        await target.AddPathsAsync(request.Paths);
    }

    /// <summary>Raised when the window should come to the front.</summary>
    public event EventHandler? ActivateWindowRequested;

    [RelayCommand]
    private void Navigate(string page)
    {
        if (IsBusy && page is not ("Create" or "Check")) return;
        CurrentPage = page switch
        {
            "Check" => Check,
            "Settings" => SettingsPage,
            "Update" => Update,
            "About" => About,
            "Developer" => Developer,
            _ => Create
        };
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (!SettingsPage.CanChangeTheme) return;
        Theme.CurrentThemeVariant = Theme.IsDark ? AppThemeVariant.Light : AppThemeVariant.Dark;
    }

    /// <summary>The update banner's View button; like the navigation buttons it waits until no batch is running.</summary>
    [RelayCommand]
    private void ShowUpdate()
    {
        if (!IsBusy) CurrentPage = Update;
    }

    [RelayCommand]
    private void DismissNotice() => NoticeText = null;

    // Keyboard shortcuts act on the current list page.
    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (CurrentList is { } list && list.AddFilesCommand.CanExecute(null)) await list.AddFilesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (CurrentList is { } list && list.AddFolderCommand.CanExecute(null)) await list.AddFolderCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (CurrentList is { } list && list.RunAllCommand.CanExecute(null)) await list.RunAllCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void CancelAll()
    {
        if (CurrentList is { } list && list.CancelCommand.CanExecute(null)) list.CancelCommand.Execute(null);
    }

    [RelayCommand]
    private void FocusSearch() => CurrentList?.RequestSearchFocus();
}
