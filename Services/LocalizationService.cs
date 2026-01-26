using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using Avalonia.Media;

namespace CheckHash.Services;

public partial class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    // ResourceManager -> CheckHash.Lang.Resources
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;
    private readonly CultureInfo _systemUICulture;
    private readonly CultureInfo _systemCulture;

    public List<LanguageItem> AvailableLanguages { get; } = new()
    {
        // AUTO DETECT LANGUAGE
        //---------------------------------------//
        new("Auto (System)", "auto"),  // completed (auto detect, if your system language is not supported, it will default to English)
        // ASIA LANGUAGES
        //---------------------------------------//
        new("English (US)", "en-US"), // completed

        // SOUTHEAST ASIA LANGUAGES
        new("Tiếng Việt", "vi-VN"),  // completed
        new("Bahasa Indonesia", "id-ID"),// completed
        new("ภาษาไทย (Thai)", "th-TH"),// completed
        new("Filipino (Tagalog)", "fil-PH"),// completed
        // Bahasa Malaysia
        new("Bahasa Melayu", "ms-MY"), // completed
        new("မြန်မာ (Burmese)", "my-MM"), // completed
        new("ខ្មែរ (Khmer)", "km-KH"), // completed
        new("ລາວ (Lao)", "lo-LA"), // completed
        // tet (Timor-Leste) not supported by .NET natively yet (if .NET supported this language in the future,uncomment this line)

        // MIDDLE EAST/ WEST ASIA LANGUAGES
        new("العربية (Arabic)", "ar-SA"), // completed
        new("فارسی (Persian)", "fa-IR"), // completed
        new("עברית (Hebrew)", "he-IL"),
        // Turkish
        new("Türkçe (Turkish)", "tr-TR"), // completed

        //---------------------------------------//
        // SOUTH ASIA LANGUAGES
        new("हिन्दी (Hindi)", "hi-IN"),  // completed
        new("বাংলা (Bengali)", "bn-BD"), // completed
        // Sri Lanka - Sinhala
        new("සිංහල (Sinhala)", "si-LK"), // completed
        //--------------------------------------//
        // EAST ASIA LANGUAGES
        new("Traditional Chinese (Taiwan)", "zh-hant"), // completed
        new("简体中文 (Chinese Simplified)", "zh-hans-cn"), // completed
        new("日本語 (Japanese)", "ja-jp"),// completed
        new("한국어 (Korean)", "ko-KR"), // completed
        new("Монгол (Mongolian)", "mn-MN"), // completed

        // EUROPE LANGUAGES
        //---------------------------------------//
        new("----Europe Region-------","--"), // Use to separate, no function
        new("Español (Spanish)", "es-ES"), // completed
        new("Français (French)", "fr-FR"), // completed
        new("Deutsch (German)", "de-DE"),  // completed
        // Sweden
        new("Svenska (Swedish)", "sv-SE"), // completed
        // pl - pl
        new("Polski (Polish)", "pl-PL"), // completed
        // Russian
        new("Русский (Russian)", "ru-RU"), // completed
        // Romanian
        new("Română (Romanian)", "ro-RO"), // completed
        // Ukrainian
        new("Українська (Ukrainian)", "uk-UA"), // completed
        // Croatia
        new("Hrvatski (Croatian)", "hr-HR"), // completed
        // Czech
        new("Čeština (Czech)", "cs-CZ"), // completed
        // Italy
        new("Italiano (Italian)", "it-IT"), // completed
        // Serbia (Latin)
        new("Srpski (Serbian Latin)", "sr-latn-RS"), // completed
        // Bulgaria
        new("Български (Bulgarian)", "bg-BG"), // completed
        // Denmark (chưa tính Greenland)
        new("Dansk (Danish)", "da-DK"), // completed
        // Greece
        new("Ελληνικά (Greek)", "el-GR"), // completed
        //  fi-fi
        new("Suomeksi (Finnish)", "fi-FI"), // completed
        // Hungary
        new("Magyar (Hungarian)", "hu-HU"), // completed
        // Norway
        new("Norsk (Norwegian)", "nb-NO"), // completed
        // Netherlands
        new("Nederlands (Dutch)", "nl-NL"), // completed
        // Slovenia
        new("Slovenščina (Slovenian)", "sl-SI"), // completed


        //---------------------------------------//
        // NORTH - CENTRAL AMERICA & CARIBBEAN LANGUAGES
        new("----North/Central America & Caribbean Region-------","--"), // Used to separate, no function

        // -- South America LANGUAGES
        new("----South America Region-------","--"), // Used to separate, no function
        new("Português (Brasil)", "pt-BR"), // completed
    };

    [ObservableProperty] private LanguageItem _selectedLanguage;
    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;

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

    public LocalizationService()
    {
        _systemUICulture = CultureInfo.CurrentUICulture;
        _systemCulture = CultureInfo.CurrentCulture;
        _resourceManager = new ResourceManager("CheckHash.Lang.Resources", typeof(LocalizationService).Assembly);

        _selectedLanguage = AvailableLanguages[0];
        var codeToLoad = DetectSystemLanguageCode();
        SetLanguage(codeToLoad);
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
        string fallbackCode = supportedLanguages.FirstOrDefault()?.Code ?? "en-US";

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
        catch { }

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
        var rtlLanguages = new[] { "ar", "he", "fa", "ur", "yi", "ps" };
        var parts = languageCode.Split('-');
        var twoLetterCode = parts.Length > 0 ? parts[0].ToLower() : languageCode.ToLower();

        if (rtlLanguages.Contains(twoLetterCode))
        {
            FlowDirection = FlowDirection.RightToLeft;
        }
        else
        {
            FlowDirection = FlowDirection.LeftToRight;
        }
    }
}

public record LanguageItem(string Name, string Code)
{
    public override string ToString() => Name;
}