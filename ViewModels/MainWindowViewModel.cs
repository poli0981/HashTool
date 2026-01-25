using Avalonia;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;
using CheckHash.Models;

namespace CheckHash.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private bool _isPaneOpen = true;
    
    public PreferencesService Prefs => PreferencesService.Instance;
    public LocalizationService L => LocalizationService.Instance;
    public LocalizationService Localization => LocalizationService.Instance;
    public SettingsViewModel SettingsVM { get; } = new();
    public FontService FontConfig => FontService.Instance;
    public ThemeService Theme => ThemeService.Instance;

    public string MenuCreateText => L["Menu_Create"];
    public string MenuCheckText => L["Menu_Check"];
    public string MenuSettingsText => L["Menu_Settings"];
    public string MenuUpdateText => L["Menu_Update"];
    public string MenuThemeText => L["Menu_Theme"];
    public string MenuAboutText => L["Menu_About"];
    public string MenuDeveloperText => L["Menu_Developer"]; // Thêm property này
    public string AppTitleText => L["AppTitle"];

    [RelayCommand]
    private void NavigateToSettings() => CurrentPage = SettingsVM;
    public CreateHashViewModel CreateHashVM { get; } = new();
    public CheckHashViewModel CheckHashVM { get; } = new();
    public UpdateViewModel UpdateVM { get; } = new();
    public AboutViewModel AboutVM { get; } = new();
    public DeveloperViewModel DeveloperVM { get; } = new();

    public MainWindowViewModel()
    {
        CurrentPage = CreateHashVM;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
        {
            OnPropertyChanged(nameof(Localization));
            OnPropertyChanged(nameof(MenuCreateText));
            OnPropertyChanged(nameof(MenuCheckText));
            OnPropertyChanged(nameof(MenuSettingsText));
            OnPropertyChanged(nameof(MenuUpdateText));
            OnPropertyChanged(nameof(MenuThemeText));
            OnPropertyChanged(nameof(MenuAboutText));
            OnPropertyChanged(nameof(MenuDeveloperText));
            OnPropertyChanged(nameof(AppTitleText));
        }
    }

    [RelayCommand]
    private void NavigateToCreate() => CurrentPage = CreateHashVM;

    [RelayCommand]
    private void NavigateToCheck() => CurrentPage = CheckHashVM;
    
    [RelayCommand]
    private void NavigateToUpdate() => CurrentPage = UpdateVM;

    [RelayCommand]
    private void NavigateToAbout() => CurrentPage = AboutVM;
    
    [RelayCommand]
    private void NavigateToDeveloper() => CurrentPage = DeveloperVM;

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Theme.CurrentThemeVariant == AppThemeVariant.Dark)
        {
            Theme.CurrentThemeVariant = AppThemeVariant.Light;
        }
        else
        {
            Theme.CurrentThemeVariant = AppThemeVariant.Dark;
        }
    }

    [RelayCommand]
    private async Task OpenFilePicker()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (window == null) return;

        if (CurrentPage == CreateHashVM)
        {
            await CreateHashVM.AddFilesCommand.ExecuteAsync(window);
        }
        else if (CurrentPage == CheckHashVM)
        {
            await CheckHashVM.AddFilesToCheckCommand.ExecuteAsync(window);
        }
    }

    [RelayCommand]
    private void ClearAllLists()
    {
        CreateHashVM.ClearListCommand.Execute(null);
        CheckHashVM.ClearListCommand.Execute(null);
    }

    [RelayCommand]
    private async Task HotkeyCheckAll()
    {
        if (CurrentPage == CheckHashVM && CheckHashVM.VerifyAllCommand.CanExecute(null))
        {
            await CheckHashVM.VerifyAllCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task HotkeyCreateAll()
    {
        if (CurrentPage == CreateHashVM && CreateHashVM.ComputeAllCommand.CanExecute(null))
        {
            await CreateHashVM.ComputeAllCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task HotkeyCompressAll()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (window == null) return;

        if (CurrentPage == CreateHashVM && CreateHashVM.CompressFilesCommand.CanExecute(window))
        {
            await CreateHashVM.CompressFilesCommand.ExecuteAsync(window);
        }
    }
}