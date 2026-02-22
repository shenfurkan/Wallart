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
    private readonly Random _random = new Random();

    public string ProviderName => "Victoria and Albert Museum";

    public VictoriaAndAlbertMuseumProvider(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public async Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default)
    {
        _logService.Log($"[{ProviderName}] Fetching artworks...");
        var page = _random.Next(1, 30);
        var url = $"https://api.vam.ac.uk/v2/objects/search?images_exist=1&material=painting&page_size=100&page={page}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var records = doc.RootElement.GetProperty("records");
        var count = records.GetArrayLength();
        if (count == 0)
            throw new Exception("No artworks found.");

        var record = records[_random.Next(count)];
        var id = record.GetProperty("systemNumber").GetString() ?? "";
        
        var title = record.TryGetProperty("_primaryTitle", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
        var artist = record.TryGetProperty("_primaryMaker", out var m) && m.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
        var date = record.TryGetProperty("_primaryDate", out var d) ? d.GetString() ?? "" : "";
        var rawImageId = record.GetProperty("_images").TryGetProperty("_primary_thumbnail", out var th) ? th.GetString() : null;
        
        if (string.IsNullOrEmpty(rawImageId))
            throw new Exception("No suitable image found.");

        // Sanitize the API-provided image identifier before embedding in a URL
        var imageId = SecurityHelper.SanitizeId(rawImageId);
        var imageUrl = $"https://framemark.vam.ac.uk/collections/{imageId}/full/max/0/default.jpg";
        SecurityHelper.RequireHttps(imageUrl);

        return new ArtworkResult
        {
            Id = id,
            Title = title,
            Artist = artist,
            Date = date,
            Medium = "Painting",
            ImageUrl = imageUrl,
            ProviderName = ProviderName
        };
    }
}
