using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallArt.Services;

public class WallpaperManager : IWallpaperManager
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDWININICHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        [return: MarshalAs(UnmanagedType.U4)]
        uint GetMonitorDevicePathCount();
        [return: MarshalAs(UnmanagedType.Struct)]
        Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor([MarshalAs(UnmanagedType.U4)] uint color);
        [return: MarshalAs(UnmanagedType.U4)]
        uint GetBackgroundColor();
        void SetPosition([MarshalAs(UnmanagedType.I4)] int position);
        [return: MarshalAs(UnmanagedType.I4)]
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(uint options, uint slideshowTick);
        [PreserveSig]
        int GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.I4)] int direction);
        int GetStatus();
        bool Enable(bool enable);
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly IConfigurationService _configService;
    private readonly string _cacheDirectory;
    private readonly string _exePath;

    public WallpaperManager(IConfigurationService configService)
    {
        _configService = configService;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        _cacheDirectory = Path.Combine(pictures, "Wallpaper Art");
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _exePath = process.MainModule?.FileName ?? string.Empty;
    }

    public void SetWallpaper(string imagePath)
    {
        try
        {
            var desktopWallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            uint monitorCount = desktopWallpaper.GetMonitorDevicePathCount();

            for (uint i = 0; i < monitorCount; i++)
            {
                string monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                desktopWallpaper.SetWallpaper(monitorId, imagePath);
            }
        }
        catch (Exception ex)
        {
            // Fallback to legacy API if COM fails
            Console.WriteLine($"COM IDesktopWallpaper failed, falling back to SystemParametersInfo: {ex.Message}");
            SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        }
    }

    public void ManageCache()
    {
        var bounds = _configService.Current.CacheBounds;
        if (bounds <= 0) return;
        
        var di = new DirectoryInfo(_cacheDirectory);
        var fileInfos = di.EnumerateFiles("*.jpg").Concat(di.EnumerateFiles("*.png")).ToList();

        if (fileInfos.Count > bounds)
        {
            var filesToDelete = fileInfos.OrderBy(f => f.LastWriteTime).Take(fileInfos.Count - bounds);
            foreach(var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch { /* Ignore delete errors */ }
            }
        }
    }

    public void ClearCache()
    {
        var di = new DirectoryInfo(_cacheDirectory);
        if (!di.Exists) return;

        var files = di.GetFiles("*.jpg").Concat(di.GetFiles("*.png")).ToList();
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch { /* Ignore if in use */ }
        }
    }

    public void SetAutostart(bool enable)
    {
        if (string.IsNullOrEmpty(_exePath)) return;

        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolderPath, "WallArt.lnk");

        if (enable)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = _exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(_exePath);
                shortcut.Arguments = "--autostart";
                shortcut.Description = "WallArt Daemon";
                shortcut.Save();
                
                // Keep things neat, release the COM objects
                Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create shortcut: {ex.Message}");
            }
        }
        else
        {
            if (File.Exists(shortcutPath))
            {
                try
                {
                    File.Delete(shortcutPath);
                }
                catch { /* Ignore */ }
            }
        }
    }
    
    public string? GetFallbackImagePath()
    {
        var di = new DirectoryInfo(_cacheDirectory);
        if (!di.Exists) return null;
        var file = di.GetFiles("*.jpg").Concat(di.GetFiles("*.png")).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        return file?.FullName;
    }
}
