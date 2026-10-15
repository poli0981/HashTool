using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HashTool.Core.Configuration;

/// <summary>Outcome of <see cref="SettingsStore.Load"/>.</summary>
public enum SettingsLoadResult
{
    Loaded,
    CreatedDefaults,

    /// <summary>The file could not be read; it was renamed to <c>config.json.bak-*</c> and defaults are used.</summary>
    RecoveredFromCorruption
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Saves are atomic (write a temporary file, then replace), so a crash or power
/// loss can no longer leave a truncated <c>config.json</c> behind.
/// </summary>
public sealed class SettingsStore
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(string directory, ILogger<SettingsStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = directory;
        FilePath = Path.Combine(directory, "config.json");
        _logger = logger ?? NullLogger<SettingsStore>.Instance;
    }

    /// <summary><c>LocalApplicationData/HashTool</c>, the data folder every 1.x release used.</summary>
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HashTool");

    public static string DefaultSettingsDirectory => Path.Combine(DefaultDataDirectory, "settings");

    public string Directory { get; }

    public string FilePath { get; }

    /// <summary>Hash Tool 1.1.x saved font choices only here, next to the settings folder.</summary>
    public string LegacyFontSettingsPath => Path.Combine(Path.GetDirectoryName(Directory) ?? Directory, "font_settings.json");

    public SettingsLoadResult Load(out AppSettings settings)
    {
        SettingsLoadResult result;
        if (!File.Exists(FilePath))
        {
            settings = new AppSettings();
            result = SettingsLoadResult.CreatedDefaults;
        }
        else
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                settings = (JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings()).Normalize();
                result = SettingsLoadResult.Loaded;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.LogError(ex, "Settings file {Path} could not be read; using defaults", FilePath);
                TryBackupCorruptFile();
                settings = new AppSettings();
                result = SettingsLoadResult.RecoveredFromCorruption;
            }
        }

        MigrateLegacyFontSettings(settings);
        return result;
    }

    /// <summary>
    /// Imports <c>font_settings.json</c> from 1.1.x (where font changes were never written to <c>config.json</c>), saves the
    /// merged settings and removes the old files.
    /// </summary>
    private void MigrateLegacyFontSettings(AppSettings settings)
    {
        var legacyPath = LegacyFontSettingsPath;
        if (!File.Exists(legacyPath)) return;

        try
        {
            var legacy = JsonSerializer.Deserialize(File.ReadAllText(legacyPath), SettingsJsonContext.Default.LegacyFontSettings);
            if (legacy is not null)
            {
                settings.FontFamily = string.IsNullOrWhiteSpace(legacy.FontName) || legacy.FontName.StartsWith('$') ? null : legacy.FontName;
                settings.UiScale = legacy.UiScale;
                settings.BaseFontSize = legacy.BaseFontSize;
                settings.IsAutoFont = legacy.IsAutoFont;
                settings.IsFontLocked = legacy.IsLockedFont;
                settings.Normalize();
            }

            Save(settings);
            File.Delete(legacyPath);
            var fontLogDirectory = Path.Combine(Path.GetDirectoryName(Directory) ?? Directory, "log", "fontlog");
            if (System.IO.Directory.Exists(fontLogDirectory)) System.IO.Directory.Delete(fontLogDirectory, recursive: true);
            _logger.LogInformation("Migrated font settings from {Path}", legacyPath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not migrate {Path}", legacyPath);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Synchronous save for shutdown paths.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        _gate.Wait();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            File.Move(FilePath, $"{FilePath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not back up the unreadable settings file");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LegacyFontSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Shape of the 1.1.x <c>font_settings.json</c>.</summary>
internal sealed class LegacyFontSettings
{
    public string? FontName { get; set; }
    public double UiScale { get; set; } = 1.0;
    public bool IsAutoFont { get; set; } = true;
    public bool IsLockedFont { get; set; }
    public double BaseFontSize { get; set; } = 14.0;
}
