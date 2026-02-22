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

public class MainViewModel : ViewModelBase
{
    private readonly IConfigurationService _configService;
    private readonly ILogService _logService;
    private readonly ArtProviderOrchestrator _orchestrator;
    private readonly IImageProcessingService _imageService;
    private readonly IWallpaperManager _wallpaperManager;
    private DispatcherTimer _timer;
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> Logs => _logService.Logs;

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set { _isUpdating = value; OnPropertyChanged(); }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set 
        { 
            _isPaused = value; 
            OnPropertyChanged();
            if (_isPaused) _timer.Stop();
            else _timer.Start();
            _logService.Log(value ? "Background fetching paused." : "Background fetching resumed.");
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
                ResetTimer();
            }
        }
    }

    public ObservableCollection<int> AvailableIntervals { get; } = new(new[] { 15, 30, 60, 120, 240, 1440 });

    public double BackgroundDimming
    {
        get => _configService.Current.BackgroundDimming;
        set { _configService.Update(c => c.BackgroundDimming = value); OnPropertyChanged(); }
    }

    public double BackgroundBlur
    {
        get => _configService.Current.BackgroundBlur;
        set { _configService.Update(c => c.BackgroundBlur = value); OnPropertyChanged(); }
    }

    public string TypographyPosition
    {
        get => _configService.Current.TypographyPosition;
        set { _configService.Update(c => c.TypographyPosition = value); OnPropertyChanged(); }
    }

    public double TypographyScale
    {
        get => _configService.Current.TypographyScale;
        set { _configService.Update(c => c.TypographyScale = value); OnPropertyChanged(); }
    }

    public ObservableCollection<string> AvailablePositions { get; } = new(new[] { "TopRight", "BottomRight", "TopLeft", "BottomLeft" });

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

    public ICommand UpdateCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenCacheCommand { get; }

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
        
        UpdateCommand = new RelayCommand(async _ => 
        {
            if (ActiveArtwork != null)
            {
                _configService.Update(c => {
                    if (!c.BlacklistedArtworkIds.Contains(ActiveArtwork.Id))
                        c.BlacklistedArtworkIds.Add(ActiveArtwork.Id);
                });
                _logService.Log($"Blacklisted artwork: {ActiveArtwork.Id}");
            }
            await UpdateWallpaperAsync();
        }, _ => !IsUpdating);
        
        TogglePauseCommand = new RelayCommand(_ => { IsPaused = !IsPaused; });
        ClearCacheCommand = new RelayCommand(_ => {
            _logService.Log("Clearing local wallpaper cache...");
            _wallpaperManager.ClearCache();
            _logService.Log("Cache cleared.");
        });
        RestoreCommand = new RelayCommand(_ => {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow == null || !mainWindow.IsLoaded || !mainWindow.IsVisible)
            {
                mainWindow = new MainWindow(this);
                System.Windows.Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
            }
            else
            {
                if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
                mainWindow.Activate();
            }
        });
        ExitCommand = new RelayCommand(_ => {
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

        _wallpaperManager.SetAutostart(_configService.Current.AutostartEnabled);

        // Surface any config load warning (e.g. corrupted config was reset to defaults)
        if (_configService.ConfigLoadWarning is { } warning)
            _logService.Log($"⚠ {warning}");

        _timer = new DispatcherTimer();
        _timer.Tick += async (s, e) => await UpdateWallpaperAsync();
        ResetTimer();
        
        _logService.Log("WallArt initialized.");
    }

    private void ResetTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMinutes(UpdateInterval);
        _timer.Start();
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
                var path = await _imageService.ProcessAndSaveArtworkAsync(bytes, metadata, _cts.Token);
                _wallpaperManager.SetWallpaper(path);
                
                ActiveArtwork = metadata;
                _configService.Update(c => {
                    c.ActiveArtwork = metadata;
                    c.History.Insert(0, metadata);
                    if (c.History.Count > 20)
                    {
                        c.History.RemoveAt(c.History.Count - 1);
                    }
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
            }
        }
        catch (Exception ex)
        {
            _logService.Log($"Error during update: {ex.Message}");
            SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
