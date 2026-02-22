using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallArt.Models;

namespace WallArt.Services.Providers;

public class MetropolitanMuseumOfArtProvider : IArtProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _logService;
    private readonly Random _random = new Random();

    public string ProviderName => "Metropolitan Museum of Art";

    public MetropolitanMuseumOfArtProvider(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public async Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default)
    {
        _logService.Log($"[{ProviderName}] Fetching artworks...");
        var searchUrl = "https://collectionapi.metmuseum.org/public/collection/v1/search?isHighlight=true&isPublicDomain=true&medium=Paintings&q=*";
        var searchResponse = await _httpClient.GetAsync(searchUrl, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();
        
        var searchJson = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
        using var searchDoc = JsonDocument.Parse(searchJson);
        var objectIds = searchDoc.RootElement.GetProperty("objectIDs");
        var count = objectIds.GetArrayLength();
        if (count == 0)
            throw new Exception("No artworks found.");

        var selectedId = objectIds[_random.Next(count)].GetInt32();
        
        var objectUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{selectedId}";
        var objectResponse = await _httpClient.GetAsync(objectUrl, cancellationToken);
        objectResponse.EnsureSuccessStatusCode();
        
        var objectJson = await objectResponse.Content.ReadAsStringAsync(cancellationToken);
        using var objectDoc = JsonDocument.Parse(objectJson);
        var root = objectDoc.RootElement;
        
        var imageUrl = root.GetProperty("primaryImage").GetString();
        if (string.IsNullOrEmpty(imageUrl))
            imageUrl = root.GetProperty("primaryImageSmall").GetString();
            
        if (string.IsNullOrEmpty(imageUrl))
            throw new Exception("No image found.");

        return new ArtworkResult
        {
            Id = selectedId.ToString(),
            Title = root.GetProperty("title").GetString() ?? "Unknown",
            Artist = root.GetProperty("artistDisplayName").GetString() ?? "Unknown",
            Date = root.GetProperty("objectDate").GetString() ?? "",
            Medium = root.GetProperty("medium").GetString() ?? "Painting",
            ImageUrl = imageUrl,
            ProviderName = ProviderName
        };
    }
}
