using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using System.Threading;
using Avalonia.Media;
using CheckHash.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckHash.Services;

public partial class LocalizationService : ObservableObject
{
    private readonly ResourceManager _resourceManager;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly CultureInfo _systemCulture;
    private readonly CultureInfo _systemUICulture;
    private CultureInfo _currentCulture;
    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;

    [ObservableProperty] private LanguageItem _selectedLanguage;

    private static readonly HashSet<string> _rtlLanguages = new() { "ar", "he", "fa", "ur", "yi", "ps", "dv", "ug", "ku", "sd" };

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

    public List<LanguageItem> AvailableLanguages { get; } = LoadLanguages();

    private static List<LanguageItem> LoadLanguages()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "CheckHash.Lang.Languages.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Fallback if resource not found
                return GetDefaultLanguages();
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var languages = JsonSerializer.Deserialize<List<LanguageItem>>(json, options);

            return languages ?? GetDefaultLanguages();
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log("Failed to load languages: " + ex.Message, LogLevel.Error);
            return GetDefaultLanguages();
        }
    }

    private static List<LanguageItem> GetDefaultLanguages()
    {
        return new List<LanguageItem>
        {
            new("Auto (System)", "auto"),
            new("English (US)", "en-US")
        };
    }

    public string this[string key]
    {
        get
        {
            return _cache.GetOrAdd(key, k =>
            {
                try
                {
                    var str = _resourceManager.GetString(k, _currentCulture);
                    return string.IsNullOrEmpty(str) ? $"[{k}]" : str;
                }
                catch
                {
                    return $"[{k}]";
                }
            });
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
            // Ignore and use fallback
        }

        return fallbackCode;
    }

    private void SetLanguage(string languageCode)
    {
        try
        {
            _currentCulture = new CultureInfo(languageCode);
            _cache.Clear();

            Thread.CurrentThread.CurrentCulture = _currentCulture;
            Thread.CurrentThread.CurrentUICulture = _currentCulture;

            UpdateFlowDirection(languageCode);

            OnPropertyChanged("Item[]");
        }
        catch
        {
            _currentCulture = new CultureInfo("en-US");
            _cache.Clear();
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