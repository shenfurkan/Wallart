using WallArt.Models;

namespace WallArt.Services;

public interface IImageProcessingService
{
    Task<string> ProcessAndSaveArtworkAsync(byte[] imageBytes, ArtworkResult metadata, CancellationToken cancellationToken = default);
}
