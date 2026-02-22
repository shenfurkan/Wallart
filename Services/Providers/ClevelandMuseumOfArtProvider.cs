using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallArt.Models;

namespace WallArt.Services.Providers;

public class ClevelandMuseumOfArtProvider : IArtProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _logService;
    private readonly Random _random = new Random();

    public string ProviderName => "Cleveland Museum of Art";

    public ClevelandMuseumOfArtProvider(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public async Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default)
    {
        _logService.Log($"[{ProviderName}] Fetching artworks...");
        var countUrl = "https://openaccess-api.clevelandart.org/api/artworks/?has_image=1&type=Painting&limit=1";
        var countResponse = await _httpClient.GetAsync(countUrl, cancellationToken);
        countResponse.EnsureSuccessStatusCode();
        var countJson = await countResponse.Content.ReadAsStringAsync(cancellationToken);
        using var countDoc = JsonDocument.Parse(countJson);
        var total = countDoc.RootElement.GetProperty("info").GetProperty("total").GetInt32();
        
        var skip = _random.Next(Math.Min(total, 5000));

        var url = $"https://openaccess-api.clevelandart.org/api/artworks/?has_image=1&type=Painting&limit=1&skip={skip}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
            throw new Exception("No artworks found.");

        var art = data[0];
        var id = art.GetProperty("id").GetInt32().ToString();
        var title = art.GetProperty("title").GetString() ?? "Unknown";
        var artist = "Unknown";
        if (art.TryGetProperty("creators", out var creators) && creators.GetArrayLength() > 0)
        {
             artist = creators[0].GetProperty("description").GetString() ?? "Unknown";
        }
        var date = art.GetProperty("creation_date").GetString() ?? "";
        var medium = art.GetProperty("technique").GetString() ?? "Painting";
        
        var images = art.GetProperty("images");
        var imageUrl = string.Empty;
        if (images.TryGetProperty("web", out var webImage))
        {
            imageUrl = webImage.GetProperty("url").GetString() ?? "";
        }
        else if (images.TryGetProperty("print", out var printImage))
        {
            imageUrl = printImage.GetProperty("url").GetString() ?? "";
        }
        
        if (string.IsNullOrEmpty(imageUrl))
            throw new Exception("No suitable image found.");

        return new ArtworkResult
        {
            Id = id,
            Title = title,
            Artist = artist,
            Date = date,
            Medium = medium,
            ImageUrl = imageUrl,
            ProviderName = ProviderName
        };
    }
}
