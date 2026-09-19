using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellySchedule.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellySchedule;

/// <summary>
/// Jelly Schedule: a broadcast-style TV schedule for your Jellyfin library.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Jelly Schedule";

    /// <inheritdoc />
    public override string Description => "Turns your library into a weekly TV schedule: pick your viewing evenings, line up your shows and movies, and tune in.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("7f2b1e6a-3c4d-4a5e-9b8c-2d1e0f9a7b6c");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "JellySchedule",
                DisplayName = "Jelly Schedule",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuIcon = "live_tv"
            }
        ];
    }
}
