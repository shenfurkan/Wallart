using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WallArt.Services;
using WallArt.Services.Providers;
using WallArt.ViewModels;
using System.Net.Http;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WallArt;

/// <summary>
/// Rejects any outbound HTTP (non-HTTPS) request at the HttpClient pipeline level.
/// </summary>
file sealed class HttpsEnforcingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"Security: HTTP request blocked — only HTTPS is allowed. URL: {request.RequestUri}");
        return base.SendAsync(request, cancellationToken);
    }
}

public partial class App : Application
{
    private ServiceProvider _serviceProvider;
    private H.NotifyIcon.TaskbarIcon? _trayIcon;

    public App()
    {
        this.DispatcherUnhandledException += (s, e) =>
        {
            Console.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
        };
        var services = new ServiceCollection();

        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IWallpaperManager, WallpaperManager>();
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        
        // Register the HTTPS-enforcing pipeline handler
        services.AddTransient<HttpsEnforcingHandler>();

        services.AddHttpClient("Default", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WallArt/1.0");
            // Hard 30-second timeout — prevents a slow server from hanging the app
            client.Timeout = TimeSpan.FromSeconds(30);
            // 50 MB cap — prevents memory exhaustion from a malicious oversized response
            client.MaxResponseContentBufferSize = 50L * 1024 * 1024;
        })
        .AddHttpMessageHandler<HttpsEnforcingHandler>();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

        services.AddTransient<IArtProvider, ArtInstituteOfChicagoProvider>();
        services.AddTransient<IArtProvider, ClevelandMuseumOfArtProvider>();
        services.AddTransient<IArtProvider, MetropolitanMuseumOfArtProvider>();
        services.AddTransient<IArtProvider, VictoriaAndAlbertMuseumProvider>();

        services.AddSingleton<ArtProviderOrchestrator>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Contains("--uninstall"))
        {
            RunUninstall();
            Shutdown();
            return;
        }

        // Surface any config load warning so the user can see it in the log
        var configWarning = _serviceProvider.GetRequiredService<IConfigurationService>().ConfigLoadWarning;
        if (configWarning != null)
        {
            var log = _serviceProvider.GetRequiredService<ILogService>();
            log.Log($"⚠ Config warning: {configWarning}");
        }

        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        
        // Ensure MainViewModel is initialized (starts background fetch timer)
        _ = mainViewModel.UpdateInterval;

        var uri = new Uri("pack://application:,,,/Wallart.ico");
        var streamInfo = System.Windows.Application.GetResourceStream(uri);
        System.Drawing.Icon? winApiIcon = null;
        if (streamInfo != null)
        {
            winApiIcon = new System.Drawing.Icon(streamInfo.Stream);
        }

        _trayIcon = new H.NotifyIcon.TaskbarIcon
        {
            ToolTipText = "WallArt Daemon",
            Icon = winApiIcon,
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick
        };
        
        // Tray Double Click
        _trayIcon.TrayMouseDoubleClick += (s, args) => 
        {
            mainViewModel.RestoreCommand.Execute(null);
        };
        
        // Context Menu
        var contextMenu = new System.Windows.Controls.ContextMenu();
        
        var nextItem = new System.Windows.Controls.MenuItem { Header = "Next Artwork", Command = mainViewModel.ForceUpdateCommand };
        var cacheItem = new System.Windows.Controls.MenuItem { Header = "Open Cache", Command = mainViewModel.OpenCacheCommand };
        var restoreItem = new System.Windows.Controls.MenuItem { Header = "Restore UI", Command = mainViewModel.RestoreCommand };
        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit", Command = mainViewModel.ExitCommand };
        
        contextMenu.Items.Add(nextItem);
        contextMenu.Items.Add(cacheItem);
        contextMenu.Items.Add(restoreItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);
        
        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.Visibility = Visibility.Visible;
        _trayIcon.DataContext = mainViewModel;
        
        if (!e.Args.Contains("--autostart"))
        {
            mainViewModel.RestoreCommand.Execute(null);
        }
    }

    private void RunUninstall()
    {
        try
        {
            // Remove legacy autostart registry entry (if any exists)
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("WallArt", false);
            
            // Remove new autostart shortcut
            var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = System.IO.Path.Combine(startupFolderPath, "WallArt.lnk");
            if (System.IO.File.Exists(shortcutPath))
            {
                System.IO.File.Delete(shortcutPath);
            }

            // Delete cache folder (Pictures/Wallpaper Art)
            var pictures = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Wallpaper Art");
            if (System.IO.Directory.Exists(pictures))
                System.IO.Directory.Delete(pictures, true);

            // Delete config folder (AppData/WallArt)
            var appData = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallArt");
            if (System.IO.Directory.Exists(appData))
                System.IO.Directory.Delete(appData, true);

            System.Windows.MessageBox.Show(
                "WallArt has been successfully uninstalled.\n\n• Autostart registry entry removed\n• Wallpaper cache cleared\n• Configuration deleted",
                "WallArt Uninstalled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Some items could not be removed:\n{ex.Message}",
                "WallArt Uninstall",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _serviceProvider.Dispose();
    }
}
