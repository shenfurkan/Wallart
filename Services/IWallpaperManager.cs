namespace WallArt.Services;

public interface IWallpaperManager
{
    void SetWallpaper(string imagePath);
    void ManageCache();
    void ClearCache();
    void SetAutostart(bool enable);
    string? GetFallbackImagePath();
}
