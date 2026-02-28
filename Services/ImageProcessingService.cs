using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using WallArt.Models;

namespace WallArt.Services;

public class ImageProcessingService : IImageProcessingService
{
    private readonly string _cacheDirectory;
    private readonly ILogService _logService;
    private readonly SixLabors.Fonts.FontFamily _fontFamily;

    private readonly IConfigurationService _configService;

    public ImageProcessingService(ILogService logService, IConfigurationService configService)
    {
        _logService = logService;
        _configService = configService;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        _cacheDirectory = Path.Combine(pictures, "Wallpaper Art");
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        var fontCollection = new FontCollection();
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        
        using var stream = assembly.GetManifestResourceStream("WallArt.Font.GoogleSans-Regular.ttf");
        if (stream != null)
        {
            _fontFamily = fontCollection.Add(stream);
        }
        else
        {
            if (SystemFonts.TryGet("Arial", out var fallback))
            {
                _fontFamily = fallback;
            }
            else
            {
                // Last resort fallback
                _fontFamily = SystemFonts.Families.GetEnumerator().MoveNext() ? SystemFonts.Families.GetEnumerator().Current : default;
            }
        }
    }

    public async Task<string> ProcessAndSaveArtworkAsync(byte[] imageBytes, ArtworkResult metadata, CancellationToken cancellationToken = default)
    {
        _logService.Log("Processing image...");
        using var image = SixLabors.ImageSharp.Image.Load(imageBytes);

        var targetWidth = 3840;
        var targetHeight = 2160;
        var targetRatio = (double)targetWidth / targetHeight;
        var sourceRatio = (double)image.Width / image.Height;

        var ratioDiff = Math.Abs(sourceRatio - targetRatio);

        if (ratioDiff <= 0.50)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Crop,
                Sampler = KnownResamplers.Lanczos3
            }));
        }
        else
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Pad,
                PadColor = Color.Black,
                Sampler = KnownResamplers.Lanczos3
            }));
        }



        DrawTypography(image, metadata);

        var safeId = SecurityHelper.SanitizeId(metadata.Id);
        var filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeId}.jpg";
        // Verify the resolved path stays within the cache directory
        var path = SecurityHelper.EnsurePathIsWithin(Path.Combine(_cacheDirectory, filename), _cacheDirectory);
        
        await image.SaveAsJpegAsync(path, cancellationToken);
        _logService.Log($"Image saved to cache: {filename}");
        
        return path;
    }

    private void DrawTypography(SixLabors.ImageSharp.Image image, ArtworkResult metadata)
    {
        var paddingX = image.Width * 0.03f;
        var paddingY = image.Height * 0.03f;
        var maxTextWidth = (image.Width * 0.55f) - paddingX;
        
        var text = $"{metadata.Title}\n{metadata.Artist}\n{metadata.ProviderName}";
        float fontSize = image.Width * 0.02f;
        Font font = _fontFamily.CreateFont(fontSize, FontStyle.Regular);
        
        PointF origin = new PointF(image.Width - paddingX, paddingY);

        RichTextOptions options = new RichTextOptions(font)
        {
            Origin = origin,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.End,
            WrappingLength = maxTextWidth,
            LineSpacing = 1.2f
        };

        try 
        {
            image.Mutate(x => x.DrawText(
                options,
                text,
                Brushes.Solid(Color.White)
            ));
        } catch { } // If text drawing fails gracefully bypass
    }
}
