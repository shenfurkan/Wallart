# WallArt

![WallArt](Images/header.png)

> A Windows desktop wallpaper daemon that automatically fetches and displays fine art from world-class museum collections.

WallArt runs quietly in the background and updates your desktop wallpaper on a customizable schedule, fetching high-quality artworks from public museum APIs. Each image is processed to fit your screen perfectly and is accompanied by details including the title, artist, and source institution.

---

## Features

- **Automatic Wallpaper Updates** — Fetches a new artwork at a configurable interval (default: every 60 minutes).
- **Multiple Museum Sources** — Aggregates artwork from four major public collections:
  - 🏛️ Art Institute of Chicago
  - 🏛️ Metropolitan Museum of Art (New York)
  - 🏛️ Cleveland Museum of Art
  - 🏛️ Victoria and Albert Museum (London)
- **Smart Image Processing** — Resizes and crops artwork to 4K (3840×2160) using Lanczos3 resampling; intelligently letterboxes portraits with a black background.
- **Typography Overlay** — Renders artwork metadata (title, artist, and source) directly onto the wallpaper using the Google Sans font (configurable position and scale).
- **Instant Refresh** — Skip any artwork immediately with a single click.
- **Provider Customization** — Enable or disable individual museum sources via the Settings tab.
- **Visual Enhancements** — Apply Gaussian blur and/or a dark overlay to ensure your desktop shortcuts remain legible (optional).
- **Local Cache Management** — Downloaded images are saved to `Pictures\Wallpaper Art` for offline browsing; includes a configurable cache size limit (default: 50 images).
- **Windows Autostart** — Simple registry-based integration to launch WallArt silently at login.
- **Multi-Monitor Support** — Sets consistent wallpapers across every connected monitor via the `IDesktopWallpaper` COM API, with robust fallback mechanisms.

---

## Screenshots

### Interface
![Interface](Images/interface.png)

### Example Wallpaper
![Example Wallpaper](Images/wallpaper.png)

### Museum Selection
![Museum Section](Images/choose.png)

---

## Requirements

| Requirement | Detail |
|---|---|
| OS | Windows 10 / 11 |

### Installer (Recommended)

1. Click the [Download](https://github.com/shenfurkan/Wallart/releases) button
2. Run the installer. No administrator rights are required.
3 The application will launch automatically  start on background.


## Building from Source

```powershell
git clone https://github.com/shenfurkan/Wallart.git
cd WallArt
dotnet build
```

To publish a self-contained executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

### Packaging the Installer

Requires [Inno Setup 6+](https://jrsoftware.org/isdl.php).

```powershell
ISCC.exe installer.iss
```

The installer will be generated in `installer_output\WallArt_Setup.exe`.

---

## Usage

| Action | Instructions |
|---|---|
| Open UI | Double-click the tray icon |
| Skip to next artwork | Right-click tray → **Dislike (Next)**, or click **Dislike** in the UI |
| Like current artwork | Click **Like** in the UI (appends to favorites) |
| Open image cache | Right-click tray → **Open Cache** |
| Exit | Right-click tray → **Exit** |

### Settings

| Setting | Default | Description |
|---|---|---|
| Update interval | 60 min | Frequency of wallpaper updates |
| Cache size | 50 | Maximum number of images kept locally |
| Autostart | On | Launch automatically with Windows |
| Background blur | 0 | Gaussian blur radius for the wallpaper |
| Background dimming | 0 | Dark overlay opacity (0 = none, 1 = fully black) |
| Typography position | Top Right | Alignment of the artwork label |
| Typography scale | 1.0 | Size multiplier for the text overlay |

---

## Architecture

```
WallArt/
├── App.xaml.cs                   # DI container, tray icon, and startup logic
├── MainWindow.xaml               # UI components (Now Playing, History, Settings, Log)
├── Models/
│   ├── WallArtConfig.cs          # Application configuration model
│   └── ArtworkResult.cs          # Artwork metadata and source information
├── Services/
│   ├── ConfigurationService.cs   # JSON-based configuration management
│   ├── WallpaperManager.cs       # Wallpaper application and cache management
│   ├── ImageProcessingService.cs # Image transformations and typography rendering
│   ├── LogService.cs             # Application event logging
│   └── Providers/
│       ├── IArtProvider.cs                    # Abstract provider interface
│       ├── ArtProviderOrchestrator.cs         # Provider selection and retry logic
│       ├── ArtInstituteOfChicagoProvider.cs
│       ├── MetropolitanMuseumOfArtProvider.cs
│       ├── ClevelandMuseumOfArtProvider.cs
│       └── VictoriaAndAlbertMuseumProvider.cs
└── ViewModels/
    └── MainViewModel.cs          # UI logic and reactive state management
```

---

## Uninstallation

Use **Add or Remove Programs** or run the following command from the application directory:

```powershell
WallArt.exe --uninstall
```

This removes the autostart registration and local configuration. You will be prompted to either keep or delete the image cache.

---

## Dependencies

| Package | Purpose |
|---|---|
| [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | System tray integration |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Core image processing |
| [SixLabors.ImageSharp.Drawing](https://github.com/SixLabors/ImageSharp.Drawing) | Typography and overlay rendering |
| [SixLabors.Fonts](https://github.com/SixLabors/Fonts) | Custom font support |
| Microsoft.Extensions.DependencyInjection | Dependency injection |
| Microsoft.Extensions.Http | HTTP client management |

---

## Data & Privacy

WallArt interacts exclusively with public museum APIs. No personal data is collected or transmitted. Downloaded images are stored locally in your `Pictures\Wallpaper Art` directory.

---

## License

This project is open-source. See the [LICENSE](LICENSE) file for more details.

