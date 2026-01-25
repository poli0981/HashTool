using Xunit;
using CheckHash.ViewModels;
using CheckHash.Services;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Linq;

namespace CheckHash.Tests;

public class LocalizationTests
{
    [Fact]
    public void UpdateViewModel_ShouldUpdateProperties_WhenLanguageChanges()
    {
        // Arrange
        var vm = new UpdateViewModel();
        bool versionChanged = false;
        bool localizationChanged = false;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentVersionText))
            {
                versionChanged = true;
            }
            if (e.PropertyName == nameof(vm.Localization))
            {
                localizationChanged = true;
            }
        };

        // Act
        // Switch to a different language
        var targetLangCode = LocalizationService.Instance.SelectedLanguage.Code == "vi-VN" ? "en-US" : "vi-VN";
        var targetLang = LocalizationService.Instance.AvailableLanguages.First(x => x.Code == targetLangCode);

        LocalizationService.Instance.SelectedLanguage = targetLang;

        // Assert
        Assert.True(versionChanged, "CurrentVersionText property changed event should be raised");
        Assert.True(localizationChanged, "Localization property changed event should be raised");
    }

    [Fact]
    public void SettingsViewModel_ShouldNotifyLocalizationChanged_WhenLanguageChanges()
    {
        // Arrange
        var vm = new SettingsViewModel();
        bool notified = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.Localization))
            {
                notified = true;
            }
        };

        // Act
        var targetLangCode = LocalizationService.Instance.SelectedLanguage.Code == "vi-VN" ? "en-US" : "vi-VN";
        var targetLang = LocalizationService.Instance.AvailableLanguages.First(x => x.Code == targetLangCode);
        LocalizationService.Instance.SelectedLanguage = targetLang;

        // Assert
        Assert.True(notified, "SettingsViewModel should raise PropertyChanged for 'Localization'");
    }

    [Fact]
    public void DeveloperViewModel_ShouldNotifyLocalizationChanged_WhenLanguageChanges()
    {
        // Arrange
        var vm = new DeveloperViewModel();
        bool notified = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.Localization))
            {
                notified = true;
            }
        };

        // Act
        var targetLangCode = LocalizationService.Instance.SelectedLanguage.Code == "vi-VN" ? "en-US" : "vi-VN";
        var targetLang = LocalizationService.Instance.AvailableLanguages.First(x => x.Code == targetLangCode);
        LocalizationService.Instance.SelectedLanguage = targetLang;

        // Assert
        Assert.True(notified, "DeveloperViewModel should raise PropertyChanged for 'Localization'");
    }
}
