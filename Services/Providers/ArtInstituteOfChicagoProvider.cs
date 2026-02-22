using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WallArt.Models;

namespace WallArt.Services.Providers;

public class ArtInstituteOfChicagoProvider : IArtProvider
{
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;
    private readonly Random _random = new Random();

    public string ProviderName => "Art Institute of Chicago";

    public ArtInstituteOfChicagoProvider(ILogService logService, HttpClient httpClient)
    {
        _logService = logService;
        _httpClient = httpClient;
    }

    public async Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default)
    {
        _logService.Log($"[{ProviderName}] Fetching artworks...");
        
        int attempts = 0;
        Exception? lastException = null;

        while (attempts < 3)
        {
            attempts++;
            try
            {
                var page = _random.Next(1, 100);
                var url = $"https://api.artic.edu/api/v1/artworks/search?q=painting&limit=40&fields=title,image_id,thumbnail,artist_title,is_public_domain,artwork_type_title&page={page}";

                _logService.Log($"[{ProviderName}] Fetching artwork list...");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("AIC-User-Agent", "WallArtClient/1.1 (contact: je.s.se.ldial.4@gmail.com)");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("Referer", "https://www.artic.edu/");

                var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
                if (!httpResponse.IsSuccessStatusCode)
                    throw new Exception($"API request failed: {httpResponse.StatusCode}");

                var json = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("error", out var errProp))
                {
                   throw new Exception($"API Error: {errProp.ToString().Substring(0, Math.Min(errProp.ToString().Length, 150))}");
                }
                
                var data = doc.RootElement.GetProperty("data");
                var count = data.GetArrayLength();
                if (count == 0)
                {
                    lastException = new Exception("No artworks found on this page.");
                    continue;
                }

                var indices = System.Linq.Enumerable.Range(0, count).OrderBy(x => _random.Next()).ToList();
                
                foreach (var i in indices)
                {
                    var art = data[i];
                    
                    var isPublicDomain = art.TryGetProperty("is_public_domain", out var pubProp) && pubProp.ValueKind == JsonValueKind.True;
                    if (!isPublicDomain) continue;
                    
                    var typeTitle = art.TryGetProperty("artwork_type_title", out var typeProp) ? typeProp.GetString() : null;
                    if (typeTitle != "Painting") continue;
                    
                    var rawImageId = art.TryGetProperty("image_id", out var imgProp) ? imgProp.GetString() : null;
                    if (string.IsNullOrEmpty(rawImageId)) continue;

                    // Sanitize API-provided image ID before embedding in a URL
                    var imageId = SecurityHelper.SanitizeId(rawImageId);
                    var imageUrl = $"https://www.artic.edu/iiif/2/{imageId}/full/843,/0/default.jpg";
                    SecurityHelper.RequireHttps(imageUrl);

                    return new ArtworkResult
                    {
                        Id = imageId,
                        Title = art.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown Title" : "Unknown Title",
                        Artist = art.TryGetProperty("artist_title", out var artistProp) ? artistProp.GetString() ?? "Unknown Artist" : "Unknown Artist",
                        Date = "",
                        Medium = typeTitle ?? "Painting",
                        ImageUrl = imageUrl,
                        ProviderName = ProviderName
                    };
                }
                
                lastException = new Exception("No suitable public domain painting found on this page.");
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logService.Log($"[{ProviderName}] Attempt {attempts} failed: {ex.Message}");
            }
        }
        
        throw lastException ?? new Exception("Failed to fetch artwork after retries.");
    }
}
