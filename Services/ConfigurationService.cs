using System;
using System.IO;
using System.Text.Json;
using CheckHash.Models;

namespace CheckHash.Services;

public class ConfigurationService
{
    private readonly string _configDir;

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configDir = Path.Combine(appData, "CheckHash", "log", "settings");
        ConfigPath = Path.Combine(_configDir, "config.json");
    }

    public static ConfigurationService Instance { get; } = new();

    public string ConfigPath { get; }

    public void Save(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(_configDir)) Directory.CreateDirectory(_configDir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to save config: {ex.Message}", LogLevel.Error);
        }
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                return config ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Log($"Failed to load config: {ex.Message}", LogLevel.Error);
        }

        return new AppConfig();
    }

    public void EnsureConfigFileExists()
    {
        if (!File.Exists(ConfigPath)) Save(new AppConfig());
    }
}