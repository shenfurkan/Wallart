using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using WallArt.Models;

namespace WallArt.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly string _configPath;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private WallArtConfig _currentConfig;

    /// <inheritdoc />
    public string? ConfigLoadWarning { get; private set; }

    public WallArtConfig Current
    {
        get
        {
            _lock.EnterReadLock();
            try   { return _currentConfig; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WallArt");
        Directory.CreateDirectory(dir); // no-op if already exists
        _configPath = Path.Combine(dir, "config.json");
        _currentConfig = LoadConfig();
    }

    private WallArtConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
            return new WallArtConfig();

        // Use a plain read lock — no nested lock needed here, we are not yet published
        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<WallArtConfig>(json);
            if (config != null)
            {
                config.Validate();
                return config;
            }

            ConfigLoadWarning = "Config file was empty or null — defaults applied.";
        }
        catch (Exception ex)
        {
            // Record the warning so MainViewModel can show it via the log,
            // but do NOT crash: fall through and return a fresh default config.
            ConfigLoadWarning = $"Config could not be loaded ({ex.GetType().Name}: {ex.Message}). Defaults applied.";
        }

        return new WallArtConfig();
    }

    public void Update(Action<WallArtConfig> updateAction)
    {
        _lock.EnterWriteLock();
        try
        {
            // Deep-clone via JSON round-trip so the update lambda cannot mutate the live instance
            var json = JsonSerializer.Serialize(_currentConfig);
            var newConfig = JsonSerializer.Deserialize<WallArtConfig>(json) ?? new WallArtConfig();

            updateAction(newConfig);
            newConfig.Validate();  // clamp any values the caller may have set out of range

            var newJson = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, newJson);

            _currentConfig = newConfig;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
