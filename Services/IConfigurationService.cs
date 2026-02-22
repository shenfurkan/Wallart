using WallArt.Models;

namespace WallArt.Services;

public interface IConfigurationService
{
    WallArtConfig Current { get; }

    /// <summary>
    /// Non-null when the config file could not be deserialized and defaults were applied.
    /// Consumers should log this to inform the user.
    /// </summary>
    string? ConfigLoadWarning { get; }

    void Update(Action<WallArtConfig> updateAction);
}
