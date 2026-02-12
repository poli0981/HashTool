using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CheckHash.Models;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheckHash.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private bool _isPaneOpen = true;

    public MainWindowViewModel()
    {
        CurrentPage = CreateHashVM;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;

        CreateHashVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateHashViewModel.IsComputing))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIdle));
            }
        };

        CheckHashVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CheckHashViewModel.IsChecking))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsIdle));
            }
        };
    }

    public PreferencesService Prefs => PreferencesService.Instance;
    public LocalizationService L => LocalizationService.Instance;
    public LocalizationService Localization => LocalizationService.Instance;
    private SettingsViewModel? _settingsVM;
    public SettingsViewModel SettingsVM => _settingsVM ??= new SettingsViewModel();

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

    private CreateHashViewModel? _createHashVM;
    public CreateHashViewModel CreateHashVM
    {
        get
        {
            if (_createHashVM == null)
            {
                _createHashVM = new CreateHashViewModel();
                _createHashVM.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CreateHashViewModel.IsComputing))
                    {
                        OnPropertyChanged(nameof(IsBusy));
                        OnPropertyChanged(nameof(IsIdle));
                    }
                };
            }
            return _createHashVM;
        }
    }

    private CheckHashViewModel? _checkHashVM;
    public CheckHashViewModel CheckHashVM
    {
        get
        {
            if (_checkHashVM == null)
            {
                _checkHashVM = new CheckHashViewModel();
                _checkHashVM.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CheckHashViewModel.IsChecking))
                    {
                        OnPropertyChanged(nameof(IsBusy));
                        OnPropertyChanged(nameof(IsIdle));
                    }
                };
            }
            return _checkHashVM;
        }
    }

    private UpdateViewModel? _updateVM;
    public UpdateViewModel UpdateVM => _updateVM ??= new UpdateViewModel();

    private AboutViewModel? _aboutVM;
    public AboutViewModel AboutVM => _aboutVM ??= new AboutViewModel();

    private DeveloperViewModel? _developerVM;
    public DeveloperViewModel DeveloperVM => _developerVM ??= new DeveloperViewModel();

    public bool IsBusy => (_createHashVM?.IsComputing ?? false) || (_checkHashVM?.IsChecking ?? false);
    public bool IsIdle => !IsBusy;

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentPage = SettingsVM;
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
    private void NavigateToCreate()
    {
        CurrentPage = CreateHashVM;
    }

    [RelayCommand]
    private void NavigateToCheck()
    {
        CurrentPage = CheckHashVM;
    }

    [RelayCommand]
    private void NavigateToUpdate()
    {
        CurrentPage = UpdateVM;
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        CurrentPage = AboutVM;
    }

    [RelayCommand]
    private void NavigateToDeveloper()
    {
        CurrentPage = DeveloperVM;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (!SettingsVM.CanChangeTheme) return;

        if (Theme.CurrentThemeVariant == AppThemeVariant.Dark)
            Theme.CurrentThemeVariant = AppThemeVariant.Light;
        else
            Theme.CurrentThemeVariant = AppThemeVariant.Dark;
    }

    [RelayCommand]
    private async Task OpenFilePicker()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (window == null) return;

        if (CurrentPage == CreateHashVM)
            await CreateHashVM.AddFilesCommand.ExecuteAsync(window);
        else if (CurrentPage == CheckHashVM) await CheckHashVM.AddFilesCommand.ExecuteAsync(window);
    }

    [RelayCommand]
    private void ClearAllLists()
    {
        _createHashVM?.ClearListCommand.Execute(null);
        _checkHashVM?.ClearListCommand.Execute(null);
    }

    [RelayCommand]
    private async Task HotkeyCheckAll()
    {
        if (CurrentPage == CheckHashVM && CheckHashVM.VerifyAllCommand.CanExecute(null))
            await CheckHashVM.VerifyAllCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task HotkeyCreateAll()
    {
        if (CurrentPage == CreateHashVM && CreateHashVM.ComputeAllCommand.CanExecute(null))
            await CreateHashVM.ComputeAllCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task HotkeyCompressAll()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (window == null) return;

        if (CurrentPage == CreateHashVM && CreateHashVM.CompressFilesCommand.CanExecute(window))
            await CreateHashVM.CompressFilesCommand.ExecuteAsync(window);
    }
}