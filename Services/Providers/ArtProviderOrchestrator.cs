using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using WallArt.Models;

namespace WallArt.Services.Providers;

public class ArtProviderOrchestrator
{
    // Static to avoid re-allocating this array on every wallpaper-change attempt
    private static readonly string[] _excludeWords =
        { "vase", "pottery", "ceramic", "vessel", "bowl", "plate", "cup", "dish", "urn", "jar", "pitcher" };

    private readonly IEnumerable<IArtProvider> _providers;
    private readonly IConfigurationService _configService;
    private readonly ILogService _logService;

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

            var isPreferred = Random.Shared.Next(100) < 80;
            List<IArtProvider> shuffledProviders;
            
            if (isPreferred)
            {
                var preferred = activeProviders.Where(p => p.ProviderName.Contains("Chicago") || p.ProviderName.Contains("Metropolitan"))
                                          .OrderBy(_ => Random.Shared.Next()).ToList();
                var others = activeProviders.Where(p => !p.ProviderName.Contains("Chicago") && !p.ProviderName.Contains("Metropolitan"))
                                       .OrderBy(_ => Random.Shared.Next()).ToList();
                shuffledProviders = preferred.Concat(others).ToList();
            }
            else
            {
                shuffledProviders = activeProviders.OrderBy(_ => Random.Shared.Next()).ToList();
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
                        if (_excludeWords.Any(w => titleLower.Contains(w) || mediumLower.Contains(w)))
                        {
                            throw new Exception($"Skipping non-fine-art object '{artwork.Title}' ({artwork.Medium}).");
                        }
                        
                        var blacklist = _configService.Current.BlacklistedArtworkIds;
                        if (blacklist.Contains(artwork.Id))
                        {
                            throw new Exception($"Skipping blacklisted artwork: {artwork.Id}");
                        }

                    _logService.Log($"[{provider.ProviderName}] Selected: {artwork.Title} by {artwork.Artist}");
                    HttpResponseMessage response;
                    if (artwork.ImageUrl.Contains("artic.edu"))
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, artwork.ImageUrl);
                        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36");
                        request.Headers.TryAddWithoutValidation("AIC-User-Agent", "WallArtClient/1.1 (contact: je.s.se.ldial.4@gmail.com)");
                        request.Headers.TryAddWithoutValidation("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                        request.Headers.TryAddWithoutValidation("Referer", "https://www.artic.edu/");
                        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    }
                    else
                    {
                        response = await _httpClient.GetAsync(artwork.ImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    }

                    byte[] bytes;
                    using (response)
                    {
                        response.EnsureSuccessStatusCode();
                        var ct = response.Content.Headers.ContentType?.MediaType ?? "";
                        if (!ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                            throw new Exception($"Unexpected Content-Type '{ct}' — expected an image.");

                        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var ms = new MemoryStream();
                        byte[] buffer = new byte[8192];
                        int bytesRead;
                        bool identified = false;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                            if (!identified && ms.Length >= 8192 && _configService.Current.PreferHorizontalImages)
                            {
                                ms.Position = 0;
                                var info = Image.Identify(ms);
                                ms.Position = ms.Length; // restore
                                if (info != null)
                                {
                                    identified = true;
                                    if (info.Width < info.Height)
                                    {
                                        throw new Exception($"Skipping portrait image {info.Width}×{info.Height} — retrying.");
                                    }
                                }
                            }
                        }

                        // One last check in case the image was very small or identify failed earlier
                        if (!identified && _configService.Current.PreferHorizontalImages)
                        {
                            ms.Position = 0;
                            var info = Image.Identify(ms);
                            if (info != null && info.Width < info.Height)
                            {
                                throw new Exception($"Skipping portrait image {info.Width}×{info.Height} — retrying.");
                            }
                        }
                        
                        bytes = ms.ToArray();
                    }
                    if (artwork.ImageUrl.Contains("artic.edu"))
                    {
                        _logService.Log($"[{provider.ProviderName}] Downloaded {bytes.Length / 1024}KB");
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
