using WallArt.Models;

namespace WallArt.Services;

public interface IArtProvider
{
    string ProviderName { get; }
    Task<ArtworkResult> FetchHorizontalArtworkAsync(CancellationToken cancellationToken = default);
}
