using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using WallArt.Commands;
using WallArt.Models;
using WallArt.Services;
using WallArt.Services.Providers;

namespace WallArt.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILogService _logService;
    private readonly ArtProviderOrchestrator _orchestrator;
    private readonly IImageProcessingService _imageService;
    private readonly IWallpaperManager _wallpaperManager;
    private SmartScheduler _scheduler;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _debugTimer;

    public string DebugNextRun => _scheduler != null ? _scheduler.NextRunTime.ToString("yyyy-MM-dd HH:mm:ss") : "Not Scheduled";
    public string DebugAppUptime => (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");
    public string DebugMemory => $"{System.GC.GetTotalMemory(false) / 1024 / 1024} MB";
    public string DebugStatus => IsUpdating ? "Fetching Artwork..." : (IsPaused ? "Paused" : "Idle (Waiting)");

    public ObservableCollection<string> Logs => _logService.Logs;

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set 
        { 
            _isUpdating = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(DebugStatus));
        }
    }

    public bool IsPaused
    {
        get => _configService.Current.IsPaused;
        set 
        { 
            if (_configService.Current.IsPaused != value)
            {
                _configService.Update(c => c.IsPaused = value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DebugStatus));
                if (value) _scheduler.Stop();
                else _scheduler.Start();
                _logService.Log(value ? "Background fetching paused." : "Background fetching resumed.");
            }
        }
    }

    public int UpdateInterval
    {
        get => _configService.Current.UpdateIntervalMinutes;
        set
        {
            if (_configService.Current.UpdateIntervalMinutes != value)
            {
                _configService.Update(c => c.UpdateIntervalMinutes = value);
                OnPropertyChanged();
                
                // If it's -1 (Midnight), the scheduler itself needs to handle the logic.
                // We pass TimeSpan.FromMinutes(value) which would be negative, so SmartScheduler needs to understand it.
                var newInterval = value == -1 ? TimeSpan.FromTicks(-1) : TimeSpan.FromMinutes(value);
                _scheduler?.UpdateInterval(newInterval);
                
                // Immediately force an update since the user just changed the settings explicitly
                _ = UpdateWallpaperAsync();
            }
        }
    }

    public ObservableCollection<int> AvailableIntervals { get; } = new(new[] { 60, 360, 1440, -1 });

    public ObservableCollection<ArtworkResult> History => new(_configService.Current.History);
    
    // Simplistic binding for Museum Toggles. A robust solution would use a wrapper class, but these direct properties will work for statically known providers.
    public bool UseArtInstituteOfChicago
    {
        get => GetProviderToggle("Art Institute of Chicago");
        set => SetProviderToggle("Art Institute of Chicago", value);
    }
    public bool UseClevelandMuseum
    {
        get => GetProviderToggle("Cleveland Museum of Art");
        set => SetProviderToggle("Cleveland Museum of Art", value);
    }
    public bool UseMetropolitanMuseum
    {
        get => GetProviderToggle("Metropolitan Museum of Art");
        set => SetProviderToggle("Metropolitan Museum of Art", value);
    }
    public bool UseVictoriaAndAlbert
    {
        get => GetProviderToggle("Victoria and Albert Museum");
        set => SetProviderToggle("Victoria and Albert Museum", value);
    }

    public bool ShowTextOverlay
    {
        get => _configService.Current.ShowTextOverlay;
        set
        {
            if (_configService.Current.ShowTextOverlay != value)
            {
                _configService.Update(c => c.ShowTextOverlay = value);
                OnPropertyChanged();
            }
        }
    }

    public bool PreferHorizontalImages
    {
        get => _configService.Current.PreferHorizontalImages;
        set
        {
            if (_configService.Current.PreferHorizontalImages != value)
            {
                _configService.Update(c => c.PreferHorizontalImages = value);
                OnPropertyChanged();
            }
        }
    }

    // All four corners the user can choose from
    public ObservableCollection<TextOverlayPosition> AvailableTextPositions { get; } =
        new(new[] {
            TextOverlayPosition.TopRight,
            TextOverlayPosition.TopLeft,
            TextOverlayPosition.BottomRight,
            TextOverlayPosition.BottomLeft
        });

    public TextOverlayPosition TextPosition
    {
        get => _configService.Current.TextPosition;
        set
        {
            if (_configService.Current.TextPosition != value)
            {
                _configService.Update(c => c.TextPosition = value);
                OnPropertyChanged();
            }
        }
    }

    // Human-readable label shown in the ComboBox
    public static string GetTextPositionLabel(TextOverlayPosition pos) => pos switch
    {
        TextOverlayPosition.TopRight    => "Top Right (default)",
        TextOverlayPosition.TopLeft     => "Top Left",
        TextOverlayPosition.BottomRight => "Bottom Right",
        TextOverlayPosition.BottomLeft  => "Bottom Left",
        _                               => pos.ToString()
    };

    private bool GetProviderToggle(string name)
    {
        return !_configService.Current.ProviderToggles.TryGetValue(name, out var isEnabled) || isEnabled;
    }

    private void SetProviderToggle(string name, bool value)
    {
        _configService.Update(c => c.ProviderToggles[name] = value);
        OnPropertyChanged(name.Replace(" ", "")); // Trigger UI updates if needed (though naming mismatch won't auto-resolve without mapping, the explicit properties above handle it)
    }

    private ArtworkResult? _activeArtwork;
    public ArtworkResult? ActiveArtwork
    {
        get => _activeArtwork;
        set { _activeArtwork = value; OnPropertyChanged(); }
    }

    public ICommand ForceUpdateCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenCacheCommand { get; }
    public ICommand SaveLogsCommand { get; }

    public MainViewModel(
        IConfigurationService configService,
        ILogService logService,
        ArtProviderOrchestrator orchestrator,
        IImageProcessingService imageService,
        IWallpaperManager wallpaperManager)
    {
        _configService = configService;
        _logService = logService;
        _orchestrator = orchestrator;
        _imageService = imageService;
        _wallpaperManager = wallpaperManager;

        ActiveArtwork = _configService.Current.ActiveArtwork;
        
        ForceUpdateCommand = new RelayCommand(async _ => 
        {
            _logService.Log("User requested wallpaper change.");
            _scheduler?.ManualTriggerFired(); // Push background timer out
            await UpdateWallpaperAsync();
        }, _ => !IsUpdating);
        
        TogglePauseCommand = new RelayCommand(_ => { IsPaused = !IsPaused; });
        ClearCacheCommand = new RelayCommand(_ => {
            _logService.Log("Clearing local wallpaper cache...");
            _wallpaperManager.ClearCache();
            _logService.Log("Cache cleared.");
        });
        RestoreCommand = new RelayCommand(_ => {
            var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.ShowInTaskbar = true;
                mainWindow.Show();
                if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
                mainWindow.Activate();
            }
        });
        ExitCommand = new RelayCommand(_ => {
            var mWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mWindow != null)
            {
                mWindow.IsExplicitClose = true;
            }
            System.Windows.Application.Current.Shutdown();
        });
        OpenCacheCommand = new RelayCommand(_ =>
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var cacheDir = System.IO.Path.Combine(pictures, "Wallpaper Art");

            // Canonicalize and verify path stays within MyPictures before opening
            var resolvedCache    = System.IO.Path.GetFullPath(cacheDir);
            var resolvedPictures = System.IO.Path.GetFullPath(pictures) + System.IO.Path.DirectorySeparatorChar;
            if (!resolvedCache.StartsWith(resolvedPictures, StringComparison.OrdinalIgnoreCase))
            {
                _logService.Log("Security: cache path is outside Pictures folder — open aborted.");
                return;
            }

            if (System.IO.Directory.Exists(resolvedCache))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName       = resolvedCache,
                    UseShellExecute = true,
                    Verb           = "open"
                });
            }
        });
        SaveLogsCommand = new RelayCommand(_ => {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var logFile = System.IO.Path.Combine(desktop, $"WallArt_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllLines(logFile, _logService.Logs);
                _logService.Log($"Logs saved to Desktop: {System.IO.Path.GetFileName(logFile)}");
            }
            catch (Exception ex)
            {
                _logService.Log($"Failed to save logs: {ex.Message}");
            }
        });

        _wallpaperManager.SetAutostart(_configService.Current.AutostartEnabled);

        // Surface any config load warning (e.g. corrupted config was reset to defaults)
        if (_configService.ConfigLoadWarning is { } warning)
            _logService.Log($"⚠ {warning}");

        var initialInterval = UpdateInterval == -1 ? TimeSpan.FromTicks(-1) : TimeSpan.FromMinutes(UpdateInterval);
        _scheduler = new SmartScheduler(UpdateWallpaperAsync, initialInterval, _logService);
        if (!IsPaused)
        {
            _scheduler.Start();
        }

        _debugTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _debugTimer.Tick += (s, e) => {
            OnPropertyChanged(nameof(DebugNextRun));
            OnPropertyChanged(nameof(DebugAppUptime));
            OnPropertyChanged(nameof(DebugMemory));
        };
        _debugTimer.Start();
        
        // On boot check if we are overdue
        bool isOverdue;
        if (UpdateInterval == -1)
        {
            isOverdue = _configService.Current.LastUpdateTime.Date < DateTime.Now.Date && _configService.Current.LastUpdateTime != DateTime.MinValue;
        }
        else
        {
            var sinceLastUpdate = DateTime.Now - _configService.Current.LastUpdateTime;
            isOverdue = sinceLastUpdate.TotalMinutes >= UpdateInterval;
        }

        if (isOverdue && !IsPaused)
        {
            _logService.Log("App launched and interval has already expired. Fetching immediately...");
            _ = UpdateWallpaperAsync();
        }
        
        _logService.Log("WallArt initialized with Smart System Scheduler.");
    }

    private async Task UpdateWallpaperAsync()
    {
        if (IsUpdating) return;
        IsUpdating = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            _logService.Log("Starting background update...");
            var result = await _orchestrator.GetNextArtworkAsync(_cts.Token);
            var metadata = result.Metadata;
            var bytes = result.ImageBytes;

            if (metadata != null && bytes != null)
            {
                var path = await _imageService.ProcessAndSaveArtworkAsync(bytes, metadata, _cts.Token, showText: ShowTextOverlay);
                _wallpaperManager.SetWallpaper(path);
                
                ActiveArtwork = metadata;
                _configService.Update(c => {
                    c.ActiveArtwork = metadata;
                    c.History.Insert(0, metadata);
                    if (c.History.Count > 20)
                    {
                        c.History.RemoveAt(c.History.Count - 1);
                    }
                    c.LastUpdateTime = DateTime.Now;
                });
                
                _wallpaperManager.ManageCache();
                _logService.Log("Update successful.");
            }
            else
            {
                _logService.Log("Update failed. Checking local cache...");
                var fallbackInfo = _wallpaperManager.GetFallbackImagePath();
                if (fallbackInfo != null)
                {
                    _wallpaperManager.SetWallpaper(fallbackInfo);
                    _logService.Log($"Applied fallback: {fallbackInfo}");
                }
                else
                {
                    _logService.Log("No local cache available for fallback.");
                }
                _scheduler?.ScheduleTemporaryRetry(TimeSpan.FromMinutes(5));
            }
        }
        catch (Exception ex)
        {
            _logService.Log($"Error during update: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _scheduler?.Dispose();
        GC.SuppressFinalize(this);
    }
}
