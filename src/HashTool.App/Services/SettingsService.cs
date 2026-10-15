using System;
using System.Threading;
using System.Threading.Tasks;
using HashTool.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Services;

/// <summary>
/// The single in-memory copy of the user's settings. Changes go through <see cref="Update"/>, which raises
/// <see cref="Changed"/> and saves shortly afterwards (several quick changes produce one write).
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsStore _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly Timer _saveTimer;
    private readonly Lock _gate = new();
    private int _pendingSave;

    public SettingsService(SettingsStore store, ILogger<SettingsService> logger)
    {
        _store = store;
        _logger = logger;
        LoadResult = store.Load(out var settings);
        Current = settings;
        _saveTimer = new Timer(_ => _ = SaveNowAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("Settings {Result} from {Path}", LoadResult, store.FilePath);
    }

    public AppSettings Current { get; }

    public SettingsLoadResult LoadResult { get; }

    public string FilePath => _store.FilePath;

    public string Directory => _store.Directory;

    /// <summary>Raised after <see cref="Update"/> applied a change.</summary>
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            change(Current);
            Current.Normalize();
        }

        ScheduleSave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ScheduleSave()
    {
        Interlocked.Exchange(ref _pendingSave, 1);
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes pending changes immediately (call on exit).</summary>
    public void Flush()
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (Interlocked.Exchange(ref _pendingSave, 0) == 0) return;
        try
        {
            _store.SaveJson(Snapshot());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving settings failed");
        }
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }

    /// <summary>Serialises under the lock, so a save on the timer thread never sees a half-applied change.</summary>
    private string Snapshot()
    {
        lock (_gate) return SettingsStore.Serialize(Current);
    }

    private async Task SaveNowAsync()
    {
        if (Interlocked.Exchange(ref _pendingSave, 0) == 0) return;
        try
        {
            await _store.SaveJsonAsync(Snapshot()).ConfigureAwait(false);
            _logger.LogDebug("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving settings failed");
        }
    }
}
