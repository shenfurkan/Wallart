using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallArt.Models;
using WallArt.Services;

namespace WallArt.Services.Providers;

public class VictoriaAndAlbertMuseumProvider : IArtProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _logService;


    public string ProviderName => "Victoria and Albert Museum";

    public VictoriaAndAlbertMuseumProvider(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public async Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default)
    {
        _logService.Log($"[{ProviderName}] Fetching artworks...");
        var page = Random.Shared.Next(1, 30);
        var url = $"https://api.vam.ac.uk/v2/objects/search?images_exist=1&material=painting&page_size=100&page={page}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var records = doc.RootElement.GetProperty("records");
        var count = records.GetArrayLength();
        if (count == 0)
            throw new Exception("No artworks found.");

        var record = records[Random.Shared.Next(count)];

        // Fix 3: Use systemNumber as the file-safe ID (alphanumeric by definition)
        var id = SecurityHelper.SanitizeId(record.GetProperty("systemNumber").GetString());

        var title  = record.TryGetProperty("_primaryTitle",  out var t) ? t.GetString() ?? "Unknown" : "Unknown";
        var artist = record.TryGetProperty("_primaryMaker",  out var m) &&
                     m.TryGetProperty("name", out var n)               ? n.GetString() ?? "Unknown" : "Unknown";
        var date   = record.TryGetProperty("_primaryDate",   out var d) ? d.GetString() ?? "" : "";

        // Fix 3: _primary_thumbnail is a URL path fragment containing '/' — do NOT sanitize it as an ID.
        // Instead retrieve it raw, upgrade the size qualifier to full/max, and validate with RequireHttps.
        string? imageUrl = null;
        if (record.TryGetProperty("_images", out var imgs) &&
            imgs.TryGetProperty("_primary_thumbnail", out var thumb))
        {
            var raw = thumb.GetString();
            if (!string.IsNullOrEmpty(raw))
            {
                // The thumbnail URL returned by the API is already HTTPS and full.
                // Replace the small thumbnail size segment with the full-resolution equivalent.
                imageUrl = raw
                    .Replace("/!100,100/", "/full/max/")
                    .Replace("/!200,200/", "/full/max/");

                // If the field is just a path fragment (no scheme), prepend the CDN base.
                if (!imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    imageUrl = "https://framemark.vam.ac.uk/collections/" + imageUrl.TrimStart('/');
            }
        }

        if (string.IsNullOrEmpty(imageUrl))
            throw new Exception("No suitable image found.");

        SecurityHelper.RequireHttps(imageUrl);

        return new ArtworkResult
        {
            Id         = id,
            Title      = title,
            Artist     = artist,
            Date       = date,
            Medium     = "Painting",
            ImageUrl   = imageUrl,
            ProviderName = ProviderName
        };
    }
}
