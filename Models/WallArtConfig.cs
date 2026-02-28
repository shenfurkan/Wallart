using System;
using System.Collections.Generic;
using System.Linq;

namespace WallArt.Models;

public class WallArtConfig
{
    public int UpdateIntervalMinutes { get; set; } = 60;
    public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;
    public bool AutostartEnabled { get; set; } = true;
    public int CacheBounds { get; set; } = 50;
    public ArtworkResult? ActiveArtwork { get; set; }

    public List<string> BlacklistedArtworkIds { get; set; } = new();
    public Dictionary<string, bool> ProviderToggles { get; set; } = new();
    public List<ArtworkResult> History { get; set; } = new();

    /// <summary>
    /// Clamps all configuration values to sane bounds.  
    /// Called after deserialization so a hand-edited or corrupted config cannot crash the app.
    /// </summary>
    public void Validate()
    {
        int[] allowedIntervals = { 60, 360, 1440 };
        if (!allowedIntervals.Contains(UpdateIntervalMinutes))
            UpdateIntervalMinutes = 60;
            
        CacheBounds           = Math.Clamp(CacheBounds, 0, 1000);

        BlacklistedArtworkIds ??= new();
        ProviderToggles       ??= new();
        History               ??= new();

        // Cap history size so the config file cannot grow without bound
        if (History.Count > 100)
            History = History.Take(100).ToList();
    }
}
