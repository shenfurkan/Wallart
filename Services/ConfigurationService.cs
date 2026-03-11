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

        try
        {
            // Fix 8: Reject oversized config files before reading into memory
            var fileInfo = new FileInfo(_configPath);
            if (fileInfo.Length > 5 * 1024 * 1024)
            {
                ConfigLoadWarning = "Config file exceeds 5 MB size limit — defaults applied.";
                return new WallArtConfig();
            }

            var json = File.ReadAllText(_configPath);
            // Fix 8: Bounded JSON depth prevents stack exhaustion from crafted configs
            var opts = new JsonSerializerOptions { MaxDepth = 8 };
            var config = JsonSerializer.Deserialize<WallArtConfig>(json, opts);
            if (config != null)
            {
                config.Validate();
                return config;
            }

            ConfigLoadWarning = "Config file was empty or null — defaults applied.";
        }
        catch (Exception ex)
        {
            ConfigLoadWarning = $"Config could not be loaded ({ex.GetType().Name}: {ex.Message}). Defaults applied.";
        }

        return new WallArtConfig();
    }

    public void Update(Action<WallArtConfig> updateAction)
    {
        _lock.EnterWriteLock();
        try
        {
            // Fix 8: Same bounded options used for all (de)serialization
            var opts = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 8 };

            // Deep-clone via JSON round-trip so the update lambda cannot mutate the live instance
            var json = JsonSerializer.Serialize(_currentConfig, opts);
            var newConfig = JsonSerializer.Deserialize<WallArtConfig>(json, opts) ?? new WallArtConfig();

            updateAction(newConfig);
            newConfig.Validate(); // clamp any values the caller may have set out of range

            var newJson = JsonSerializer.Serialize(newConfig, opts);

            // Fix 4: Atomic write — write to .tmp then replace, so a crash mid-write
            // never leaves a half-written (corrupt) config.json on disk.
            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, newJson);
            File.Move(tempPath, _configPath, overwrite: true);

            _currentConfig = newConfig;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
