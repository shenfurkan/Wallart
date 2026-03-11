using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using WallArt.Models;

namespace WallArt.Services;

public class ImageProcessingService : IImageProcessingService
{
    // Cached encoder: quality 90 for sharp 4K wallpapers; static to avoid per-call allocation
    private static readonly JpegEncoder _jpegEncoder = new JpegEncoder { Quality = 90 };

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
                _fontFamily = SystemFonts.Families.FirstOrDefault();
            }
        }
    }

    public async Task<string> ProcessAndSaveArtworkAsync(byte[] imageBytes, ArtworkResult metadata, CancellationToken cancellationToken = default, bool showText = true)
    {
        _logService.Log("Processing image...");
        using var image = SixLabors.ImageSharp.Image.Load(imageBytes);

        // Fix 1: Decompression bomb guard — reject images whose pixel area exceeds 2× 4K
        const long MaxPixels = (long)3840 * 2160 * 2;
        if ((long)image.Width * image.Height > MaxPixels)
            throw new InvalidOperationException(
                $"Security: image dimensions {image.Width}×{image.Height} exceed safe limit.");

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



        if (showText)
            DrawTypography(image, metadata);

        var safeId = SecurityHelper.SanitizeId(metadata.Id);
        var filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeId}.jpg";
        // Verify the resolved path stays within the cache directory
        var path = SecurityHelper.EnsurePathIsWithin(Path.Combine(_cacheDirectory, filename), _cacheDirectory);
        
        await image.SaveAsJpegAsync(path, _jpegEncoder, cancellationToken);
        _logService.Log($"Image saved to cache: {filename}");
        
        return path;
    }

    // Fix 7: Strip control characters and Unicode bidi/RTL overrides from API-sourced strings
    private static string SanitizeDisplayText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        // Remove C0/C1 controls and known bidirectional override codepoints
        var cleaned = new string(input.Where(c =>
            !char.IsControl(c) &&
            c != '\u200B' && c != '\u202A' && c != '\u202B' &&
            c != '\u202C' && c != '\u202D' && c != '\u202E' &&
            c != '\u2066' && c != '\u2067' && c != '\u2069').ToArray());
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }

    private void DrawTypography(SixLabors.ImageSharp.Image image, ArtworkResult metadata)
    {
        var paddingX = image.Width  * 0.03f;
        var paddingY = image.Height * 0.03f;
        var maxTextWidth = (image.Width * 0.55f) - paddingX;

        var text = $"{SanitizeDisplayText(metadata.Title)}\n" +
                   $"{SanitizeDisplayText(metadata.Artist)}\n" +
                   $"{SanitizeDisplayText(metadata.ProviderName)}";
        float fontSize = image.Width * 0.02f;
        Font font = _fontFamily.CreateFont(fontSize, FontStyle.Regular);

        var position = _configService.Current.TextPosition;

        // Derive corner geometry from the chosen position
        bool isRight  = position == WallArt.Models.TextOverlayPosition.TopRight  ||
                        position == WallArt.Models.TextOverlayPosition.BottomRight;
        bool isBottom = position == WallArt.Models.TextOverlayPosition.BottomLeft ||
                        position == WallArt.Models.TextOverlayPosition.BottomRight;

        float originX = isRight  ? image.Width  - paddingX : paddingX;
        float originY = isBottom ? image.Height - paddingY : paddingY;

        var hAlign    = isRight  ? HorizontalAlignment.Right  : HorizontalAlignment.Left;
        var vAlign    = isBottom ? VerticalAlignment.Bottom   : VerticalAlignment.Top;
        var tAlign    = isRight  ? TextAlignment.End          : TextAlignment.Start;

        RichTextOptions options = new RichTextOptions(font)
        {
            Origin              = new PointF(originX, originY),
            HorizontalAlignment = hAlign,
            VerticalAlignment   = vAlign,
            TextAlignment       = tAlign,
            WrappingLength      = maxTextWidth,
            LineSpacing         = 1.2f
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
