using System.Collections.Generic;

namespace HashTool.App.Services;

/// <summary>
/// Indexer wrapper bound from XAML as <c>Localization[Key]</c>. View models replace their instance when the language changes,
/// which makes every binding through it re-evaluate.
/// </summary>
public sealed class LocalizationProxy(LocalizationService service)
{
    public string this[string key] => service[key];

    public List<LanguageItem> AvailableLanguages => service.AvailableLanguages;

    public string Format(string key, params object?[] args) => service.Format(key, args);
}
