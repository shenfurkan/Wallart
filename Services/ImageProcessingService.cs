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
    private readonly FontFamily _fontFamily;

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
        using var image = Image.Load(imageBytes);

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

        var blur = _configService.Current.BackgroundBlur;
        var dimming = _configService.Current.BackgroundDimming;

        image.Mutate(x =>
        {
            if (blur > 0)
            {
                x.GaussianBlur((float)blur);
            }
            if (dimming > 0)
            {
                x.Fill(Color.Black.WithAlpha((float)dimming));
            }
        });

        DrawTypography(image, metadata);

        // Sanitize the API-provided ID before using it in a filename to prevent path traversal
        var safeId = SecurityHelper.SanitizeId(metadata.Id);
        var filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeId}.jpg";
        // Verify the resolved path stays within the cache directory
        var path = SecurityHelper.EnsurePathIsWithin(Path.Combine(_cacheDirectory, filename), _cacheDirectory);
        
        await image.SaveAsJpegAsync(path, cancellationToken);
        _logService.Log($"Image saved to cache: {filename}");
        
        return path;
    }

    private void DrawTypography(Image image, ArtworkResult metadata)
    {
        var paddingX = image.Width * 0.03f;
        var paddingY = image.Height * 0.03f;
        var maxTextWidth = (image.Width * 0.55f) - paddingX;
        
        var text = $"{metadata.Title}\n{metadata.Artist}\n{metadata.ProviderName}";
        
        var scale = (float)_configService.Current.TypographyScale;
        float fontSize = (image.Width * 0.02f) * scale;
        
        Font font = _fontFamily.CreateFont(fontSize, FontStyle.Regular);
        
        var position = _configService.Current.TypographyPosition;
        PointF origin;
        HorizontalAlignment hAlign;
        VerticalAlignment vAlign;
        TextAlignment tAlign;

        switch (position)
        {
            case "TopLeft":
                origin = new PointF(paddingX, paddingY);
                hAlign = HorizontalAlignment.Left;
                vAlign = VerticalAlignment.Top;
                tAlign = TextAlignment.Start;
                break;
            case "BottomLeft":
                origin = new PointF(paddingX, image.Height - paddingY);
                hAlign = HorizontalAlignment.Left;
                vAlign = VerticalAlignment.Bottom;
                tAlign = TextAlignment.Start;
                break;
            case "BottomRight":
                origin = new PointF(image.Width - paddingX, image.Height - paddingY);
                hAlign = HorizontalAlignment.Right;
                vAlign = VerticalAlignment.Bottom;
                tAlign = TextAlignment.End;
                break;
            case "TopRight":
            default:
                origin = new PointF(image.Width - paddingX, paddingY);
                hAlign = HorizontalAlignment.Right;
                vAlign = VerticalAlignment.Top;
                tAlign = TextAlignment.End;
                break;
        }

        RichTextOptions options = new RichTextOptions(font)
        {
            Origin = origin,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
            TextAlignment = tAlign,
            WrappingLength = maxTextWidth,
            LineSpacing = 1.2f
        };

        while (fontSize > 10 * scale)
        {
            font = _fontFamily.CreateFont(fontSize, FontStyle.Regular);
            options.Font = font;
            var box = TextMeasurer.MeasureSize(text, new TextOptions(font) { WrappingLength = options.WrappingLength });
            
            if (box.Width <= maxTextWidth)
            {
                break;
            }
            fontSize -= (2f * scale);
        }

        image.Mutate(x => x.DrawText(
            options,
            text,
            Brushes.Solid(Color.White)
        ));
    }
}
