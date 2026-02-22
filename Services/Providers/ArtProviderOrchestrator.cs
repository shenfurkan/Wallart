using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WallArt.Models;

namespace WallArt.Services.Providers;

public class ArtProviderOrchestrator
{
    private readonly IEnumerable<IArtProvider> _providers;
    private readonly IConfigurationService _configService;
    private readonly ILogService _logService;
    private readonly Random _random = new Random();
    private readonly HttpClient _httpClient;

    public ArtProviderOrchestrator(IEnumerable<IArtProvider> providers, IConfigurationService configService, ILogService logService, HttpClient httpClient)
    {
        _providers = providers;
        _configService = configService;
        _logService = logService;
        _httpClient = httpClient;
    }

    public async Task<(ArtworkResult? Metadata, byte[]? ImageBytes)> GetNextArtworkAsync(CancellationToken cancellationToken = default)
    {
            var activeToggles = _configService.Current.ProviderToggles;
            var activeProviders = _providers.Where(p => 
                !activeToggles.TryGetValue(p.ProviderName, out var isEnabled) || isEnabled).ToList();

            if (!activeProviders.Any())
            {
                _logService.Log("All providers are disabled in settings.");
                return (null, null);
            }

            var isPreferred = _random.Next(100) < 80;
            List<IArtProvider> shuffledProviders;
            
            if (isPreferred)
            {
                var preferred = activeProviders.Where(p => p.ProviderName.Contains("Chicago") || p.ProviderName.Contains("Metropolitan"))
                                          .OrderBy(x => _random.Next()).ToList();
                var others = activeProviders.Where(p => !p.ProviderName.Contains("Chicago") && !p.ProviderName.Contains("Metropolitan"))
                                       .OrderBy(x => _random.Next()).ToList();
                shuffledProviders = preferred.Concat(others).ToList();
            }
            else
            {
                shuffledProviders = activeProviders.OrderBy(x => _random.Next()).ToList();
            }

            foreach (var provider in shuffledProviders)
            {
                int attempts = 0;
                while (attempts < 3)
                {
                    attempts++;
                    try
                    {
                        var artwork = await provider.FetchHorizontalArtworkAsync(cancellationToken);
                        
                        var titleLower = artwork.Title.ToLower();
                        var mediumLower = artwork.Medium.ToLower();
                        var excludeWords = new[] { "vase", "pottery", "ceramic", "vessel", "bowl", "plate", "cup", "dish", "urn", "jar", "pitcher" };
                        if (excludeWords.Any(w => titleLower.Contains(w) || mediumLower.Contains(w)))
                        {
                            throw new Exception($"Skipping non-fine-art object '{artwork.Title}' ({artwork.Medium}).");
                        }
                        
                        var blacklist = _configService.Current.BlacklistedArtworkIds;
                        if (blacklist.Contains(artwork.Id))
                        {
                            throw new Exception($"Skipping blacklisted artwork: {artwork.Id}");
                        }

                    _logService.Log($"[{provider.ProviderName}] Selected: {artwork.Title} by {artwork.Artist}");
                    
                    _logService.Log($"[{provider.ProviderName}] Downloading image...");
                    
                    byte[] bytes;
                    if (artwork.ImageUrl.Contains("artic.edu"))
                    {
                        // Use HttpClient with AIC-specific headers instead of spawning curl.exe
                        using var request = new HttpRequestMessage(HttpMethod.Get, artwork.ImageUrl);
                        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36");
                        request.Headers.TryAddWithoutValidation("AIC-User-Agent", "WallArtClient/1.1 (contact: je.s.se.ldial.4@gmail.com)");
                        request.Headers.TryAddWithoutValidation("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                        request.Headers.TryAddWithoutValidation("Referer", "https://www.artic.edu/");
                        var response = await _httpClient.SendAsync(request, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        _logService.Log($"[{provider.ProviderName}] Downloaded {bytes.Length / 1024}KB");
                    }
                    else
                    {
                        var response = await _httpClient.GetAsync(artwork.ImageUrl, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    }
                    
                    return (artwork, bytes);
                }
                catch (Exception ex)
                {
                    if (attempts == 3)
                        _logService.Log($"[{provider.ProviderName}] Failed after 3 attempts: {ex.Message}");
                }
            }
        }
        
        _logService.Log("All providers failed.");
        return (null, null);
    }
}
