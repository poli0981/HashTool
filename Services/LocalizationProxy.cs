using System.Collections.Generic;

namespace CheckHash.Services;

public class LocalizationProxy
{
    private readonly LocalizationService _service;

    public LocalizationProxy(LocalizationService service)
    {
        _service = service;
    }

    public string this[string key] => _service[key];

    public List<LanguageItem> AvailableLanguages => _service.AvailableLanguages;

    public LanguageItem SelectedLanguage
    {
        get => _service.SelectedLanguage;
        set => _service.SelectedLanguage = value;
    }
}