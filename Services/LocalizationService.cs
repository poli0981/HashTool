using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class LocalizationService : ObservableObject
{
    // ResourceManager -> CheckHash.Lang.Resources
    private readonly ResourceManager _resourceManager;
    private readonly CultureInfo _systemCulture;
    private readonly CultureInfo _systemUICulture;
    private CultureInfo _currentCulture;
    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;

    [ObservableProperty] private LanguageItem _selectedLanguage;

    private static readonly string[] _rtlLanguages = { "ar", "he", "fa", "ur", "yi", "ps", "dv", "ug", "ku", "sd" };

    public LocalizationService()
    {
        _systemUICulture = CultureInfo.CurrentUICulture;
        _systemCulture = CultureInfo.CurrentCulture;
        _resourceManager = new ResourceManager("CheckHash.Lang.Resources", typeof(LocalizationService).Assembly);

        _selectedLanguage = AvailableLanguages[0];
        var codeToLoad = DetectSystemLanguageCode();
        SetLanguage(codeToLoad);
    }

    public static LocalizationService Instance { get; } = new();

    public List<LanguageItem> AvailableLanguages { get; } = new()
    {
        // AUTO DETECT LANGUAGE
        //---------------------------------------//
        new LanguageItem("Auto (System)", "auto"),
        // DEFAULT LANGUAGE (IF NO MATCH FOUND)
        //---------------------------------------//
        new LanguageItem("English (US)", "en-US"),
        // ARAB LANGUAGE (Use ar for all arab countries)
        new LanguageItem("العربية (Arabic)", "ar"),

        // SOUTHEAST ASIA LANGUAGES
        new LanguageItem("Tiếng Việt", "vi-VN"),
        new LanguageItem("Bahasa Indonesia", "id-ID"),
        new LanguageItem("ภาษาไทย (Thai)", "th-TH"),
        new LanguageItem("Filipino (Tagalog)", "fil-PH"),
        new LanguageItem("Bahasa Melayu (Malaysia)", "ms-MY"),
        new LanguageItem("English (Singapore)", "en-sg"),
        new LanguageItem("简体中文 (Singapore)", "zh-hans-sg"),
        new LanguageItem("မြန်မာ (Burmese)", "my-MM"),
        new LanguageItem("ខ្មែរ (Khmer)", "km-KH"),
        new LanguageItem("ລາວ (Laos)", "lo-LA"),

        // MIDDLE EAST/ WEST ASIA LANGUAGES
        new LanguageItem("فارسی (Persian)", "fa-IR"),
        new LanguageItem("עברית (Hebrew)", "he-IL"),
        new LanguageItem("Türkçe (Turkish)", "tr-TR"),
        new LanguageItem("Ελληνικά", "el-CY"),

        //---------------------------------------//
        // SOUTH ASIA LANGUAGES
        new LanguageItem("हिन्दी (Hindi)", "hi-IN"),
        new LanguageItem("English (India)", "en-IN"),
        new LanguageItem("বাংলা (Bengali)", "bn-BD"),
        new LanguageItem("සිංහල (Sinhala)", "si-LK"),
        new LanguageItem("རྫོང་ཁ (Dzongkha)", "dz-BT"),
        new LanguageItem("नेपाली (Nepali)", "ne-NP"),
        new LanguageItem("ދިވެހި (Divehi)", "dv-MV"),
        new LanguageItem("پښتو (Pashto)", "ps-AF"),
        //--------------------------------------//
        // EAST ASIA LANGUAGES
        new LanguageItem("繁體中文 (Chinese Traditional)", "zh-hant"),
        new LanguageItem("简体中文 (Chinese Simplified)", "zh-hans-cn"),
        new LanguageItem("日本語 (Japanese)", "ja-jp"),
        new LanguageItem("한국어 (Korean)", "ko-KR"),
        new LanguageItem("Монгол (Mongolian)", "mn-MN"),

        new LanguageItem("Қазақша (Kazakh)", "kk-KZ"),
        new LanguageItem("Кыргызча (Kyrgyz)", "ky-KG"),
        new LanguageItem("Тоҷикӣ (Tajik)", "tg-TJ"),
        new LanguageItem("Türkmençe (Turkmen)", "tk-TM"),

        // EUROPE LANGUAGES
        new LanguageItem("Español (Spanish)", "es-ES"),
        new LanguageItem("Español (Latam)", "es-419"),
        new LanguageItem("Français (French)", "fr-FR"),
        new LanguageItem("Deutsch (German)", "de-DE"),
        new LanguageItem("Svenska (Swedish)", "sv-SE"),
        new LanguageItem("Bosanski (Bosnian Latin)", "bs-latn-ba"),
        new LanguageItem("Polski (Polish)", "pl-PL"),
        new LanguageItem("Русский (Russian)", "ru-RU"),
        new LanguageItem("Deutsch (Austria)", "de-AT"),
        new LanguageItem("Deutsch (Switzerland)", "de-CH"),
        new LanguageItem("Shqip (Albanian)", "sq-AL"),
        new LanguageItem("Македонски (Macedonian)", "mk-MK"),
        new LanguageItem("Română (Romanian)", "ro-RO"),
        new LanguageItem("Slovenčina (Slovak)", "sk-SK"),
        new LanguageItem("Українська (Ukrainian)", "uk-UA"),
        new LanguageItem("Hrvatski (Croatian)", "hr-HR"),
        new LanguageItem("Čeština (Czech)", "cs-CZ"),
        new LanguageItem("Italiano (Italian)", "it-IT"),
        new LanguageItem("Српски (Serbian Cyrillic)", "sr-cyrl-rs"),
        new LanguageItem("Български (Bulgarian)", "bg-BG"),
        new LanguageItem("Dansk (Danish)", "da-DK"),
        new LanguageItem("Ελληνικά (Greek)", "el-GR"),
        new LanguageItem("Suomeksi (Finnish)", "fi-FI"),
        new LanguageItem("Magyar (Hungarian)", "hu-HU"),
        new LanguageItem("Norsk (Norwegian)", "nb-NO"),
        new LanguageItem("Nederlands (Dutch)", "nl-NL"),
        new LanguageItem("Nederlands (Belgium)", "nl-BE"),
        new LanguageItem("Íslenska (Icelandic)", "is-IS"),
        new LanguageItem("Slovenščina (Slovenian)", "sl-SI"),
        new LanguageItem("Eesti (Estonian)", "et-EE"),
        new LanguageItem("Latviešu (Latvian)", "lv-LV"),
        new LanguageItem("Lietuvių (Lithuanian)", "lt-LT"),
        new LanguageItem("Português (Brasil)", "pt-BR")
    };

    // Indexer
    public string this[string key]
    {
        get
        {
            try
            {
                var str = _resourceManager.GetString(key, _currentCulture);
                return string.IsNullOrEmpty(str) ? $"[{key}]" : str;
            }
            catch
            {
                return $"[{key}]";
            }
        }
    }

    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        var codeToLoad = value.Code == "auto" ? DetectSystemLanguageCode() : value.Code;
        SetLanguage(codeToLoad);
        FontService.Instance.SetFontForLanguage(codeToLoad);
    }

    private string DetectSystemLanguageCode()
    {
        var supportedLanguages = AvailableLanguages.Skip(1).ToList();
        var fallbackCode = supportedLanguages.FirstOrDefault()?.Code ?? "en-US";

        try
        {
            var candidates = new[] { _systemUICulture, _systemCulture };

            foreach (var culture in candidates)
            {
                if (culture == null) continue;

                var exactMatch = supportedLanguages.FirstOrDefault(x =>
                    x.Code.Equals(culture.Name, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null) return exactMatch.Code;

                var twoLetterMatch = supportedLanguages.FirstOrDefault(x =>
                    x.Code.StartsWith(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
                if (twoLetterMatch != null) return twoLetterMatch.Code;
            }
        }
        catch
        {
        }

        return fallbackCode;
    }

    private void SetLanguage(string languageCode)
    {
        try
        {
            _currentCulture = new CultureInfo(languageCode);

            // Update Thread Culture
            Thread.CurrentThread.CurrentCulture = _currentCulture;
            Thread.CurrentThread.CurrentUICulture = _currentCulture;

            UpdateFlowDirection(languageCode);

            // Update UI bindings
            OnPropertyChanged("Item[]");
        }
        catch
        {
            _currentCulture = new CultureInfo("en-US");
            OnPropertyChanged("Item[]");
        }
    }

    private void UpdateFlowDirection(string languageCode)
    {
        var parts = languageCode.Split('-');
        var twoLetterCode = parts.Length > 0 ? parts[0].ToLower() : languageCode.ToLower();

        if (_rtlLanguages.Contains(twoLetterCode))
            FlowDirection = FlowDirection.RightToLeft;
        else
            FlowDirection = FlowDirection.LeftToRight;
    }
}

public record LanguageItem(string Name, string Code)
{
    public override string ToString()
    {
        return Name;
    }
}