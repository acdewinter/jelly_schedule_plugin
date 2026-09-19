using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellySchedule.Configuration;

/// <summary>
/// Plugin configuration (integration settings). The schedule itself lives in the plugin data store.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the Sonarr base URL, e.g. http://sonarr:8989.</summary>
    public string SonarrUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the Sonarr API key.</summary>
    public string SonarrApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the Radarr base URL, e.g. http://radarr:7878.</summary>
    public string RadarrUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the Radarr API key.</summary>
    public string RadarrApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether TVmaze (no API key required) is used as a fallback for airing dates.</summary>
    public bool EnableTvMaze { get; set; } = true;
}
