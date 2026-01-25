using System;
using System.IO;
using System.Text.Json;
using CheckHash.Models;

namespace CheckHash.Services;

public class ConfigurationService
{
    public static ConfigurationService Instance { get; } = new();

    private readonly string _configPath;
    private readonly string _configDir;

    public string ConfigPath => _configPath;

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configDir = Path.Combine(appData, "CheckHash", "log", "settings");
        _configPath = Path.Combine(_configDir, "config.json");
    }

    public void Save(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(_configDir))
            {
                Directory.CreateDirectory(_configDir);
            }

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
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
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
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
        if (!File.Exists(_configPath))
        {
            Save(new AppConfig());
        }
    }
}