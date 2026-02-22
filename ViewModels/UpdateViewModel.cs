using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CheckHash.Services;
using CheckHash.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;

namespace CheckHash.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService = UpdateService.Instance;
    [ObservableProperty] private ObservableCollection<string> _availableVersions = new();

    [ObservableProperty] private string _currentVersionText;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDevChannelEnabled = true;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private LocalizationProxy _localization = new(LocalizationService.Instance);
    [ObservableProperty] private string _rollbackVersion;

    [ObservableProperty] private int _selectedChannelIndex;
    [ObservableProperty] private string? _selectedRollbackVersion;
    [ObservableProperty] private string _statusMessage;

    public UpdateViewModel()
    {
        CurrentVersionText = string.Format(L["Lbl_CurrentVersion"], _updateService.CurrentVersion);
        StatusMessage = L["Lbl_Status_Ready"];

        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                CurrentVersionText = string.Format(L["Lbl_CurrentVersion"], _updateService.CurrentVersion);
                Localization = new LocalizationProxy(LocalizationService.Instance);
            }
        };

        CheckPreReleasesAvailability();
        LoadAvailableVersions();
    }

    private LocalizationService L => LocalizationService.Instance;
    private LoggerService Logger => LoggerService.Instance;

    private async void CheckPreReleasesAvailability()
    {
        IsDevChannelEnabled = await _updateService.HasPreReleasesAsync();
    }

    private async void LoadAvailableVersions()
    {
        try
        {
            var versions = await _updateService.GetAvailableVersionsAsync();
            AvailableVersions.Clear();
            foreach (var v in versions)
            {
                if (v != _updateService.CurrentVersion.ToString())
                {
                    AvailableVersions.Add(v);
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    async partial void OnSelectedChannelIndexChanged(int value)
    {
        if (value == 1) // Developer Channel
        {
            if (!IsDevChannelEnabled)
            {
                SelectedChannelIndex = 0;
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_NoPreRelease"], MessageBoxIcon.Warning);
                return;
            }

            var accepted = await ShowDisclaimer();
            if (!accepted)
            {
                SelectedChannelIndex = 0;
                return;
            }

            Logger.Log("Switched to Developer Channel.", LogLevel.Warning);
        }
        else
        {
            Logger.Log("Switched to Stable Channel.");
        }

        await CheckUpdate();
    }

    private async Task<bool> ShowDisclaimer()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == null) return false;

            var dialog = new DisclaimerWindow();
            await dialog.ShowDialog(desktop.MainWindow);
            return dialog.IsAccepted;
        }

        return false;
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        Logger.Log("Checking for updates...");
        IsChecking = true;
        StatusMessage = L["Status_Checking"];
        IsUpdateAvailable = false;

        try
        {
            var netStatus = await _updateService.CheckConnectivityAsync();

            if (netStatus != NetworkStatus.Connected)
            {
                var errorKey = netStatus switch
                {
                    NetworkStatus.NoConnection => "Error_Network",
                    NetworkStatus.ServerError => "Error_Server",
                    NetworkStatus.ClientError => "Error_Client",
                    NetworkStatus.ApiLimitExceeded => "Error_ApiLimit",
                    _ => "Msg_Error"
                };

                var msg = L[errorKey];
                Logger.Log($"Network check failed: {netStatus}", LogLevel.Error);
                StatusMessage = string.Format(L["Status_CheckError"], msg);
                await MessageBoxHelper.ShowAsync(L["Msg_Error"], msg, MessageBoxIcon.Error);
                return;
            }

            var isDev = SelectedChannelIndex == 1;
            var updateInfo = await _updateService.CheckForUpdatesAsync(isDev);

            if (updateInfo != null)
            {
                var version = updateInfo.TargetFullRelease.Version;
                var versionString = version.ToString();
                var isPreRelease = versionString.Contains('-'); // Simple check for pre-release tag

                if (isDev)
                {
                    if (!isPreRelease)
                    {
                        Logger.Log(
                            $"Dev Mode: Update found {versionString} is not a pre-release. Keeping current version.");
                        StatusMessage = L["Msg_NoPreRelease"];
                        await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"], L["Msg_NoPreRelease"],
                            MessageBoxIcon.Information);
                        return;
                    }

                    // Pre-release available
                    IsUpdateAvailable = true;
                    StatusMessage = L["Status_NewVersion"];
                    Logger.Log($"New pre-release found: {versionString}", LogLevel.Warning);

                    var notes = await _updateService.GetReleaseNotesAsync(versionString);
                    var result = await MessageBoxHelper.ShowConfirmationAsync(L["Title_Disclaimer"],
                        string.Format(L["Msg_PreReleaseWarning"], versionString, notes), L["Btn_Install"], L["Btn_No"],
                        MessageBoxIcon.Warning, true);

                    if (result)
                    {
                        await InstallUpdate(updateInfo);
                    }

                    return;
                }

                // Stable Channel
                IsUpdateAvailable = true;
                StatusMessage = L["Status_NewVersion"];

                Logger.Log($"New version found: {versionString}", LogLevel.Success);

                var notesStable = await _updateService.GetReleaseNotesAsync(versionString);

                var resultStable = await MessageBoxHelper.ShowConfirmationAsync(L["Msg_UpdateTitle"],
                    string.Format(L["Msg_UpdateContent"], versionString, notesStable), L["Btn_Install"], L["Btn_No"],
                    MessageBoxIcon.Question, true);

                if (resultStable)
                {
                    await InstallUpdate(updateInfo);
                }
            }
            else
            {
                if (isDev)
                {
                    StatusMessage = L["Msg_NoPreRelease"];
                    Logger.Log("Dev Mode: No pre-release found.");
                    await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"], L["Msg_NoPreRelease"],
                        MessageBoxIcon.Information);
                }
                else
                {
                    StatusMessage = L["Status_Latest"];
                    Logger.Log("Application is up to date.");
                    await MessageBoxHelper.ShowAsync(L["Msg_Result_Title"], L["Msg_NoUpdate"],
                        MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L["Status_CheckError"], ex.Message);
            Logger.Log($"Update check failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!IsDownloading)
            {
                IsChecking = false;
            }
        }
    }

    [RelayCommand]
    private async Task Rollback()
    {
        if (string.IsNullOrWhiteSpace(SelectedRollbackVersion))
        {
            await MessageBoxHelper.ShowAsync(L["Msg_Error"], L["Msg_InvalidVersion"], MessageBoxIcon.Error);
            return;
        }

        Logger.Log($"Attempting rollback to version {SelectedRollbackVersion}...");
        IsChecking = true;
        StatusMessage = L["Status_Checking"];

        try
        {
            var url = await _updateService.GetSpecificVersionUrlAsync(SelectedRollbackVersion);

            if (url != null)
            {
                var result = await MessageBoxHelper.ShowConfirmationAsync(L["Title_Rollback"],
                    string.Format(L["Msg_RollbackConfirm"], SelectedRollbackVersion), L["Btn_Install"], L["Btn_No"],
                    MessageBoxIcon.Warning);

                if (result)
                {
                    StatusMessage = L["Status_Installing"];
                    IsDownloading = true;
                    DownloadProgress = 0;

                    await _updateService.DownloadAndRunInstallerAsync(url,
                        progress => { DownloadProgress = progress; });

                    StatusMessage = L["Status_InstallerLaunched"];
                    Logger.Log("Installer launched for rollback.");
                }
                else
                {
                    Logger.Log("Rollback is cancelled.");
                }
            }
            else
            {
                Logger.Log($"Version {SelectedRollbackVersion} not found or no installer available.", LogLevel.Error);
                await MessageBoxHelper.ShowAsync(L["Msg_Error"],
                    string.Format(L["Msg_VersionNotFound"], SelectedRollbackVersion), MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L["Status_CheckError"], ex.Message);
            Logger.Log($"Rollback check failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsChecking = false;
            IsDownloading = false;
        }
    }

    private async Task InstallUpdate(UpdateInfo info)
    {
        StatusMessage = L["Status_Installing"];
        IsChecking = true;
        IsDownloading = true;
        DownloadProgress = 0;
        Logger.Log("Downloading update...");
        try
        {
            await _updateService.DownloadUpdatesAsync(info, progress => { DownloadProgress = progress; });

            StatusMessage = L["Status_UpdateRestarting"];
            _updateService.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(L["Status_InstallError"], ex.Message);
            IsChecking = false;
            IsDownloading = false;
            Logger.Log($"Install failed: {ex.Message}", LogLevel.Error);
        }
    }
}