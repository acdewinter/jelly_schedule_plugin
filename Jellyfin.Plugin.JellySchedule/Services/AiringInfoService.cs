using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.Services;

/// <summary>An episode that is (or will be) broadcast, as known by Sonarr or TVmaze.</summary>
public class UpcomingEpisode
{
    public int Season { get; set; }

    public int Episode { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset? AirDate { get; set; }

    /// <summary>True when Sonarr already has the file (only meaningful for Sonarr).</summary>
    public bool HasFile { get; set; }
}

/// <summary>Airing information for a series.</summary>
public class SeriesAiringInfo
{
    public string Source { get; set; } = string.Empty;

    public string? Status { get; set; }

    public string? Network { get; set; }

    public UpcomingEpisode? NextEpisode { get; set; }

    public List<UpcomingEpisode> Upcoming { get; set; } = [];

    /// <summary>Episodes that have aired but Sonarr has not downloaded yet.</summary>
    public List<UpcomingEpisode> AiredNotAvailable { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }

    public string? Error { get; set; }
}

/// <summary>A monitored movie in Radarr that is not available yet.</summary>
public class UpcomingMovie
{
    public int RadarrId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? TmdbId { get; set; }

    public DateTimeOffset? DigitalRelease { get; set; }

    public DateTimeOffset? PhysicalRelease { get; set; }

    public DateTimeOffset? InCinemas { get; set; }

    public string? Status { get; set; }

    public bool IsAvailable { get; set; }
}

/// <summary>Result of an integration test.</summary>
public class IntegrationStatus
{
    public string Service { get; set; } = string.Empty;

    public bool Configured { get; set; }

    public bool Ok { get; set; }

    public string? Version { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// Looks up broadcast schedules from Sonarr, falling back to TVmaze (no API key needed), and "coming soon" movies from Radarr.
/// </summary>
public sealed class AiringInfoService
{
    private static readonly TimeSpan _seriesTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan _errorTtl = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiringInfoService> _logger;
    private readonly ConcurrentDictionary<Guid, SeriesAiringInfo> _seriesCache = new();
    private readonly ConcurrentDictionary<Guid, Task<SeriesAiringInfo?>> _inflight = new();
    private (DateTimeOffset At, List<UpcomingMovie> Movies)? _radarrCache;

    public AiringInfoService(IHttpClientFactory httpClientFactory, ILogger<AiringInfoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static Configuration.PluginConfiguration Config => Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

    public static bool SonarrConfigured => !string.IsNullOrWhiteSpace(Config.SonarrUrl) && !string.IsNullOrWhiteSpace(Config.SonarrApiKey);

    public static bool RadarrConfigured => !string.IsNullOrWhiteSpace(Config.RadarrUrl) && !string.IsNullOrWhiteSpace(Config.RadarrApiKey);

    public static bool TvMazeEnabled => Config.EnableTvMaze;

    /// <summary>Returns cached info without fetching.</summary>
    public SeriesAiringInfo? Peek(Guid seriesId) => _seriesCache.TryGetValue(seriesId, out var info) ? info : null;

    public void Invalidate() => _seriesCache.Clear();

    /// <summary>Gets airing info for a series, fetching when the cache is stale.</summary>
    public Task<SeriesAiringInfo?> GetForSeriesAsync(Series series, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _seriesCache.TryGetValue(series.Id, out var cached))
        {
            var ttl = cached.Error is null ? _seriesTtl : _errorTtl;
            if (DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
            {
                return Task.FromResult<SeriesAiringInfo?>(cached);
            }
        }

        return _inflight.GetOrAdd(series.Id, _ => FetchAndCacheAsync(series, cancellationToken));
    }

    public async Task<IReadOnlyList<UpcomingMovie>> GetRadarrUpcomingAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!RadarrConfigured)
        {
            return [];
        }

        if (!forceRefresh && _radarrCache is { } cache && DateTimeOffset.UtcNow - cache.At < TimeSpan.FromHours(1))
        {
            return cache.Movies;
        }

        try
        {
            using var client = CreateClient(Config.RadarrUrl, Config.RadarrApiKey);
            var movies = await client.GetFromJsonAsync<List<RadarrMovie>>("api/v3/movie", _json, cancellationToken).ConfigureAwait(false) ?? [];
            var result = movies
                .Where(m => m.Monitored && !m.HasFile)
                .Select(m => new UpcomingMovie
                {
                    RadarrId = m.Id,
                    Title = m.Title ?? string.Empty,
                    Year = m.Year,
                    TmdbId = m.TmdbId?.ToString(CultureInfo.InvariantCulture),
                    DigitalRelease = m.DigitalRelease,
                    PhysicalRelease = m.PhysicalRelease,
                    InCinemas = m.InCinemas,
                    Status = m.Status,
                    IsAvailable = m.IsAvailable
                })
                .OrderBy(m => m.DigitalRelease ?? m.PhysicalRelease ?? m.InCinemas ?? DateTimeOffset.MaxValue)
                .ToList();
            _radarrCache = (DateTimeOffset.UtcNow, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jelly Schedule: Radarr request failed");
            return _radarrCache?.Movies ?? [];
        }
    }

    public async Task<IntegrationStatus> TestAsync(string service, string? url, string? apiKey, CancellationToken cancellationToken)
    {
        var status = new IntegrationStatus { Service = service, Configured = !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(apiKey) };
        if (string.Equals(service, "tvmaze", StringComparison.OrdinalIgnoreCase))
        {
            status.Configured = true;
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var response = await client.GetAsync(new Uri("https://api.tvmaze.com/shows/1"), cancellationToken).ConfigureAwait(false);
                status.Ok = response.IsSuccessStatusCode;
                status.Message = status.Ok ? "TVmaze reachable" : $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex)
            {
                status.Message = ex.Message;
            }

            return status;
        }

        if (!status.Configured)
        {
            status.Message = "URL or API key missing";
            return status;
        }

        try
        {
            using var client = CreateClient(url!, apiKey!);
            using var doc = await client.GetFromJsonAsync<JsonDocument>("api/v3/system/status", _json, cancellationToken).ConfigureAwait(false);
            status.Ok = true;
            if (doc is not null && doc.RootElement.TryGetProperty("version", out var v))
            {
                status.Version = v.GetString();
            }

            status.Message = $"Connected to {service} {status.Version}".Trim();
        }
        catch (Exception ex)
        {
            status.Message = ex.Message;
        }

        return status;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto) ? dto : null;
    }

    private HttpClient CreateClient(string baseUrl, string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private async Task<SeriesAiringInfo?> FetchAndCacheAsync(Series series, CancellationToken cancellationToken)
    {
        try
        {
            SeriesAiringInfo? info = null;
            var tvdbId = LibraryCatalog.ProviderId(series, "Tvdb");
            var imdbId = LibraryCatalog.ProviderId(series, "Imdb");

            if (SonarrConfigured)
            {
                try
                {
                    info = await FetchSonarrAsync(tvdbId, series.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Jelly Schedule: Sonarr lookup failed for {Series}", series.Name);
                }
            }

            if (info is null && TvMazeEnabled)
            {
                try
                {
                    info = await FetchTvMazeAsync(tvdbId, imdbId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Jelly Schedule: TVmaze lookup failed for {Series}", series.Name);
                }
            }

            info ??= new SeriesAiringInfo
            {
                Source = "none",
                Status = series.Status?.ToString(),
                Error = SonarrConfigured || TvMazeEnabled ? "Not found" : "No airing source configured"
            };
            info.FetchedAt = DateTimeOffset.UtcNow;
            _seriesCache[series.Id] = info;
            return info;
        }
        finally
        {
            _inflight.TryRemove(series.Id, out _);
        }
    }

    private async Task<SeriesAiringInfo?> FetchSonarrAsync(string? tvdbId, string seriesName, CancellationToken cancellationToken)
    {
        using var client = CreateClient(Config.SonarrUrl, Config.SonarrApiKey);
        List<SonarrSeries>? matches = null;
        if (tvdbId is not null)
        {
            matches = await client.GetFromJsonAsync<List<SonarrSeries>>("api/v3/series?tvdbId=" + Uri.EscapeDataString(tvdbId), _json, cancellationToken).ConfigureAwait(false);
        }

        if (matches is null || matches.Count == 0)
        {
            var all = await client.GetFromJsonAsync<List<SonarrSeries>>("api/v3/series", _json, cancellationToken).ConfigureAwait(false) ?? [];
            matches = all.Where(s => string.Equals(s.Title, seriesName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var match = matches.FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        var episodes = await client.GetFromJsonAsync<List<SonarrEpisode>>("api/v3/episode?seriesId=" + match.Id.ToString(CultureInfo.InvariantCulture), _json, cancellationToken).ConfigureAwait(false) ?? [];
        var now = DateTimeOffset.UtcNow;
        var mapped = episodes
            .Where(e => e.SeasonNumber > 0)
            .Select(e => new UpcomingEpisode
            {
                Season = e.SeasonNumber,
                Episode = e.EpisodeNumber,
                Title = e.Title ?? string.Empty,
                AirDate = ParseDate(e.AirDateUtc),
                HasFile = e.HasFile
            })
            .OrderBy(e => e.AirDate ?? DateTimeOffset.MaxValue)
            .ThenBy(e => e.Season)
            .ThenBy(e => e.Episode)
            .ToList();

        var upcoming = mapped.Where(e => e.AirDate.HasValue && e.AirDate.Value > now).ToList();
        return new SeriesAiringInfo
        {
            Source = "sonarr",
            Status = match.Status,
            Network = match.Network,
            NextEpisode = upcoming.FirstOrDefault(),
            Upcoming = upcoming.Take(12).ToList(),
            AiredNotAvailable = mapped.Where(e => e.AirDate.HasValue && e.AirDate.Value <= now && !e.HasFile && e.AirDate.Value > now.AddDays(-90)).ToList()
        };
    }

    private async Task<SeriesAiringInfo?> FetchTvMazeAsync(string? tvdbId, string? imdbId, CancellationToken cancellationToken)
    {
        if (tvdbId is null && imdbId is null)
        {
            return null;
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.BaseAddress = new Uri("https://api.tvmaze.com/");

        TvMazeShow? show = null;
        if (tvdbId is not null)
        {
            show = await TryGetAsync<TvMazeShow>(client, "lookup/shows?thetvdb=" + Uri.EscapeDataString(tvdbId), cancellationToken).ConfigureAwait(false);
        }

        if (show is null && imdbId is not null)
        {
            show = await TryGetAsync<TvMazeShow>(client, "lookup/shows?imdb=" + Uri.EscapeDataString(imdbId), cancellationToken).ConfigureAwait(false);
        }

        if (show is null)
        {
            return null;
        }

        var episodes = await TryGetAsync<List<TvMazeEpisode>>(client, $"shows/{show.Id.ToString(CultureInfo.InvariantCulture)}/episodes", cancellationToken).ConfigureAwait(false) ?? [];
        var now = DateTimeOffset.UtcNow;
        var upcoming = episodes
            .Where(e => e.Season > 0)
            .Select(e => new UpcomingEpisode
            {
                Season = e.Season,
                Episode = e.Number ?? 0,
                Title = e.Name ?? string.Empty,
                AirDate = ParseDate(e.Airstamp) ?? ParseDate(e.Airdate)
            })
            .Where(e => e.AirDate.HasValue && e.AirDate.Value > now)
            .OrderBy(e => e.AirDate)
            .ToList();

        return new SeriesAiringInfo
        {
            Source = "tvmaze",
            Status = show.Status,
            Network = show.Network?.Name ?? show.WebChannel?.Name,
            NextEpisode = upcoming.FirstOrDefault(),
            Upcoming = upcoming.Take(12).ToList()
        };
    }

    private async Task<T?> TryGetAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken).ConfigureAwait(false);
    }

    private sealed class SonarrSeries
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public string? Status { get; set; }

        public string? Network { get; set; }

        public int TvdbId { get; set; }
    }

    private sealed class SonarrEpisode
    {
        public int SeasonNumber { get; set; }

        public int EpisodeNumber { get; set; }

        public string? Title { get; set; }

        public string? AirDateUtc { get; set; }

        public bool HasFile { get; set; }

        public bool Monitored { get; set; }
    }

    private sealed class RadarrMovie
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public int? Year { get; set; }

        public int? TmdbId { get; set; }

        public bool Monitored { get; set; }

        public bool HasFile { get; set; }

        public bool IsAvailable { get; set; }

        public string? Status { get; set; }

        public DateTimeOffset? DigitalRelease { get; set; }

        public DateTimeOffset? PhysicalRelease { get; set; }

        public DateTimeOffset? InCinemas { get; set; }
    }

    private sealed class TvMazeShow
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Status { get; set; }

        public TvMazeNetwork? Network { get; set; }

        public TvMazeNetwork? WebChannel { get; set; }
    }

    private sealed class TvMazeNetwork
    {
        public string? Name { get; set; }
    }

    private sealed class TvMazeEpisode
    {
        public int Season { get; set; }

        public int? Number { get; set; }

        public string? Name { get; set; }

        public string? Airdate { get; set; }

        public string? Airstamp { get; set; }
    }
}
