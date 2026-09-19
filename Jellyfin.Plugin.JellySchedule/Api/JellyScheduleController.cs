using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellySchedule.Models;
using Jellyfin.Plugin.JellySchedule.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.Api;

/// <summary>
/// Jelly Schedule API.
/// </summary>
[ApiController]
[Route("JellySchedule")]
[Produces(MediaTypeNames.Application.Json)]
public class JellyScheduleController : ControllerBase
{
    private static string? _appHtml;

    private readonly ScheduleStore _store;
    private readonly ScheduleEngine _engine;
    private readonly ILibraryCatalog _catalog;
    private readonly AiringInfoService _airings;
    private readonly IAuthorizationContext _auth;
    private readonly ILogger<JellyScheduleController> _logger;

    public JellyScheduleController(
        ScheduleStore store,
        ScheduleEngine engine,
        ILibraryCatalog catalog,
        AiringInfoService airings,
        IAuthorizationContext auth,
        ILogger<JellyScheduleController> logger)
    {
        _store = store;
        _engine = engine;
        _catalog = catalog;
        _airings = airings;
        _auth = auth;
        _logger = logger;
    }

    // ---------------------------------------------------------------- web app

    /// <summary>Serves the Jelly Schedule web app as a single page (CSS and JS inlined).</summary>
    [HttpGet("app")]
    [HttpGet("app/index.html")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult GetApp()
    {
        var html = _appHtml ??= BuildAppHtml();
        if (html is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Serves a static asset of the web app.</summary>
    [HttpGet("app/{file}")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult GetAppAsset([FromRoute] string file) => ServeEmbedded(file);

    // ---------------------------------------------------------------- state

    /// <summary>Gets the schedule configuration and the caller's context.</summary>
    [HttpGet("state")]
    [Authorize]
    public async Task<ActionResult<StateResponse>> GetState()
    {
        var auth = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var data = _store.Get();
        var isAdmin = IsAdmin(auth);
        var household = _engine.ResolveHouseholdUser(data);
        var response = new StateResponse
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            Me = new UserInfo { Id = auth.UserId, Name = auth.User?.Username ?? string.Empty, IsAdmin = isAdmin },
            HouseholdUser = household is null ? null : new UserInfo { Id = household.Id, Name = household.Username, IsAdmin = household.HasPermission(PermissionKind.IsAdministrator) },
            Users = isAdmin
                ? _catalog.GetUsers().Select(u => new UserInfo { Id = u.Id, Name = u.Username, IsAdmin = u.HasPermission(PermissionKind.IsAdministrator) }).OrderBy(u => u.Name).ToList()
                : [],
            CanEdit = CanEdit(data, auth),
            Settings = data.Settings,
            Windows = data.Windows,
            Lineup = data.Lineup.OrderBy(e => e.Order).ToList(),
            MovieNight = data.MovieNight,
            Blackouts = data.Blackouts.OrderBy(b => b.From).ToList(),
            OneOffs = data.OneOffs.OrderBy(o => o.Date).ThenBy(o => o.Start).ToList(),
            Integrations = new Dictionary<string, bool>
            {
                ["Sonarr"] = AiringInfoService.SonarrConfigured,
                ["Radarr"] = AiringInfoService.RadarrConfigured,
                ["TvMaze"] = AiringInfoService.TvMazeEnabled
            },
            ServerTimeZone = ScheduleEngine.ResolveTimeZone(data.Settings.TimeZoneId).Id,
            Now = ScheduleEngine.NowIn(ScheduleEngine.ResolveTimeZone(data.Settings.TimeZoneId))
        };

        return response;
    }

    /// <summary>Gets the guide for a range of days.</summary>
    [HttpGet("guide")]
    [Authorize]
    public async Task<ActionResult<GuideResult>> GetGuide([FromQuery] string? from, [FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        var data = _store.Get();
        var tz = ScheduleEngine.ResolveTimeZone(data.Settings.TimeZoneId);
        var start = DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : ScheduleEngine.TodayIn(tz);
        return await _engine.BuildGuideAsync(start, days, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets what is on now and next.</summary>
    [HttpGet("now")]
    [Authorize]
    public async Task<ActionResult<object>> GetNow(CancellationToken cancellationToken)
    {
        var data = _store.Get();
        var tz = ScheduleEngine.ResolveTimeZone(data.Settings.TimeZoneId);
        var guide = await _engine.BuildGuideAsync(ScheduleEngine.TodayIn(tz), 1, true, cancellationToken).ConfigureAwait(false);
        return new
        {
            guide.Now,
            guide.OnNow,
            guide.UpNext,
            guide.NextWindowStart,
            Today = guide.Days.FirstOrDefault()?.Airings ?? []
        };
    }

    // ---------------------------------------------------------------- lineup

    /// <summary>Gets the lineup with live status.</summary>
    [HttpGet("lineup")]
    [Authorize]
    public async Task<ActionResult<List<LineupEntryStatus>>> GetLineup([FromQuery] bool refresh = false, CancellationToken cancellationToken = default)
        => await _engine.GetLineupStatusAsync(true, refresh, cancellationToken).ConfigureAwait(false);

    /// <summary>Adds an item to the lineup.</summary>
    [HttpPost("lineup")]
    [Authorize]
    public async Task<ActionResult<LineupEntry>> AddLineup([FromBody] AddLineupRequest request)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var item = _catalog.GetItem(request.ItemId);
        if (item is null)
        {
            return NotFound(new { Message = "Item not found" });
        }

        var kind = item switch
        {
            Series => LineupKind.Series,
            MediaBrowser.Controller.Entities.Movies.Movie => LineupKind.Movie,
            MediaBrowser.Controller.Entities.Movies.BoxSet => LineupKind.Collection,
            MediaBrowser.Controller.Playlists.Playlist => LineupKind.Playlist,
            _ => (LineupKind?)null
        };
        if (kind is null)
        {
            return BadRequest(new { Message = "Only series, movies, collections and playlists can be scheduled" });
        }

        var entry = await _store.UpdateAsync(
            d =>
            {
                var existing = d.Lineup.FirstOrDefault(e => e.ItemId == item.Id);
                if (existing is not null)
                {
                    return existing;
                }

                var e = new LineupEntry
                {
                    ItemId = item.Id,
                    Kind = kind.Value,
                    Mode = kind == LineupKind.Series ? request.Mode : PlayMode.InOrder,
                    Name = item.Name ?? string.Empty,
                    Days = Normalize(request.Days),
                    EpisodesPerAiring = Math.Clamp(request.EpisodesPerAiring, 1, 6),
                    StartFrom = ScheduleEngine.ParseEpisodeCode(request.StartFrom) is null ? null : request.StartFrom!.Trim().ToUpperInvariant(),
                    Order = d.Lineup.Count == 0 ? 0 : d.Lineup.Max(x => x.Order) + 1
                };
                d.Lineup.Add(e);
                return e;
            }).ConfigureAwait(false);
        return entry;
    }

    /// <summary>Updates a lineup entry.</summary>
    [HttpPut("lineup/{id}")]
    [Authorize]
    public async Task<ActionResult<LineupEntry>> UpdateLineup([FromRoute] Guid id, [FromBody] UpdateLineupRequest request)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var entry = await _store.UpdateAsync(
            d =>
            {
                var e = d.Lineup.FirstOrDefault(x => x.Id == id);
                if (e is null)
                {
                    return null;
                }

                if (request.Mode.HasValue && e.Kind == LineupKind.Series)
                {
                    e.Mode = request.Mode.Value;
                }

                if (request.Days is not null)
                {
                    e.Days = Normalize(request.Days);
                }

                if (request.EpisodesPerAiring.HasValue)
                {
                    e.EpisodesPerAiring = Math.Clamp(request.EpisodesPerAiring.Value, 1, 6);
                }

                if (request.ClearStartFrom)
                {
                    e.StartFrom = null;
                }
                else if (request.StartFrom is not null)
                {
                    e.StartFrom = ScheduleEngine.ParseEpisodeCode(request.StartFrom) is null ? null : request.StartFrom.Trim().ToUpperInvariant();
                }

                if (request.Paused.HasValue)
                {
                    e.Paused = request.Paused.Value;
                }

                return e;
            }).ConfigureAwait(false);
        return entry is null ? NotFound() : entry;
    }

    /// <summary>Removes a lineup entry.</summary>
    [HttpDelete("lineup/{id}")]
    [Authorize]
    public async Task<ActionResult> RemoveLineup([FromRoute] Guid id)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        await _store.UpdateAsync(d => d.Lineup.RemoveAll(x => x.Id == id)).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Reorders the lineup.</summary>
    [HttpPost("lineup/reorder")]
    [Authorize]
    public async Task<ActionResult> ReorderLineup([FromBody] ReorderRequest request)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        await _store.UpdateAsync(
            d =>
            {
                var order = 0;
                foreach (var id in request.Ids)
                {
                    var e = d.Lineup.FirstOrDefault(x => x.Id == id);
                    if (e is not null)
                    {
                        e.Order = order++;
                    }
                }

                foreach (var e in d.Lineup.Where(x => !request.Ids.Contains(x.Id)).OrderBy(x => x.Order))
                {
                    e.Order = order++;
                }
            }).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Lists the episodes of a series (for the "start from" picker).</summary>
    [HttpGet("series/{id}/episodes")]
    [Authorize]
    public ActionResult<List<EpisodeInfo>> GetEpisodes([FromRoute] Guid id)
    {
        var data = _store.Get();
        var user = _engine.ResolveHouseholdUser(data);
        if (user is null || _catalog.GetItem(id) is not Series series)
        {
            return NotFound();
        }

        var episodes = _catalog.GetEpisodes(series, user, data.Settings.IncludeSpecials);
        return episodes.Select(e => new EpisodeInfo
        {
            Id = e.Id,
            Season = e.ParentIndexNumber,
            Episode = e.IndexNumber,
            Name = e.Name ?? string.Empty,
            Watched = _catalog.IsWatched(user, e),
            RuntimeMinutes = LibraryCatalog.RuntimeMinutes(e, 0)
        }).ToList();
    }

    // ---------------------------------------------------------------- schedule configuration

    /// <summary>Replaces the viewing windows.</summary>
    [HttpPut("windows")]
    [Authorize]
    public async Task<ActionResult<List<ViewingWindow>>> PutWindows([FromBody] List<ViewingWindow> windows)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var clean = new List<ViewingWindow>();
        foreach (var w in windows)
        {
            if (!ScheduleEngine.TryParseTime(w.Start, out var s) || !ScheduleEngine.TryParseTime(w.End, out var e))
            {
                return BadRequest(new { Message = "Times must be HH:mm" });
            }

            clean.Add(new ViewingWindow
            {
                Id = w.Id == Guid.Empty ? Guid.NewGuid() : w.Id,
                Days = Normalize(w.Days),
                Start = s.ToString("HH:mm", CultureInfo.InvariantCulture),
                End = e.ToString("HH:mm", CultureInfo.InvariantCulture),
                Label = string.IsNullOrWhiteSpace(w.Label) ? null : w.Label.Trim()
            });
        }

        await _store.UpdateAsync(d =>
        {
            d.Windows = clean;
            UnfreezeFuture(d);
        }).ConfigureAwait(false);
        return clean;
    }

    /// <summary>Updates movie night settings.</summary>
    [HttpPut("movie-night")]
    [Authorize]
    public async Task<ActionResult<MovieNightSettings>> PutMovieNight([FromBody] MovieNightSettings settings)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        settings.Days = Normalize(settings.Days);
        if (!string.IsNullOrWhiteSpace(settings.StartTime))
        {
            if (!ScheduleEngine.TryParseTime(settings.StartTime, out var t))
            {
                return BadRequest(new { Message = "Start time must be HH:mm" });
            }

            settings.StartTime = t.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            settings.StartTime = null;
        }

        await _store.UpdateAsync(d => d.MovieNight = settings).ConfigureAwait(false);
        return settings;
    }

    /// <summary>Updates household settings.</summary>
    [HttpPut("settings")]
    [Authorize]
    public async Task<ActionResult<ScheduleSettings>> PutSettings([FromBody] ScheduleSettings settings)
    {
        var auth = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var data = _store.Get();
        if (!CanEdit(data, auth))
        {
            return Forbid();
        }

        var isAdmin = IsAdmin(auth);
        var result = await _store.UpdateAsync(
            d =>
            {
                var s = d.Settings;
                if (isAdmin)
                {
                    if (settings.HouseholdUserId != Guid.Empty && _catalog.GetUser(settings.HouseholdUserId) is not null)
                    {
                        s.HouseholdUserId = settings.HouseholdUserId;
                    }

                    s.AdminOnlyEditing = settings.AdminOnlyEditing;
                }

                s.TimeZoneId = string.IsNullOrWhiteSpace(settings.TimeZoneId) ? string.Empty : ScheduleEngine.ResolveTimeZone(settings.TimeZoneId).Id;
                s.SlotMinutes = settings.SlotMinutes is 15 or 30 or 60 ? settings.SlotMinutes : 30;
                s.FillMode = settings.FillMode;
                s.LiveMode = settings.LiveMode;
                s.AutoplayOnOpen = settings.AutoplayOnOpen;
                s.IncludeSpecials = settings.IncludeSpecials;
                s.RerunPicker = settings.RerunPicker;
                s.JoinGraceMinutes = Math.Clamp(settings.JoinGraceMinutes, 0, 180);
                return s;
            }).ConfigureAwait(false);
        return result;
    }

    /// <summary>Replaces the blackout (away) dates.</summary>
    [HttpPut("blackouts")]
    [Authorize]
    public async Task<ActionResult<List<Blackout>>> PutBlackouts([FromBody] List<Blackout> blackouts)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var clean = blackouts
            .Where(b => b.To >= b.From)
            .Select(b => new Blackout { Id = b.Id == Guid.Empty ? Guid.NewGuid() : b.Id, From = b.From, To = b.To, Label = string.IsNullOrWhiteSpace(b.Label) ? "Away" : b.Label.Trim() })
            .OrderBy(b => b.From)
            .ToList();
        await _store.UpdateAsync(d => d.Blackouts = clean).ConfigureAwait(false);
        return clean;
    }

    /// <summary>Adds a one-off programme.</summary>
    [HttpPost("one-offs")]
    [Authorize]
    public async Task<ActionResult<OneOff>> AddOneOff([FromBody] OneOffRequest request)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var item = _catalog.GetItem(request.ItemId);
        if (item is null)
        {
            return NotFound(new { Message = "Item not found" });
        }

        if (!ScheduleEngine.TryParseTime(request.Start, out var t))
        {
            return BadRequest(new { Message = "Start must be HH:mm" });
        }

        var one = new OneOff
        {
            Date = request.Date,
            Start = t.ToString("HH:mm", CultureInfo.InvariantCulture),
            ItemId = item.Id,
            Name = item is Episode ep ? $"{ep.SeriesName} · {ScheduleEngine.EpisodeCode(ep.ParentIndexNumber, ep.IndexNumber)}" : item.Name ?? string.Empty,
            DurationMinutes = request.DurationMinutes
        };
        await _store.UpdateAsync(d =>
        {
            d.OneOffs.RemoveAll(o => o.Date < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)));
            d.OneOffs.Add(one);
        }).ConfigureAwait(false);
        return one;
    }

    /// <summary>Removes a one-off programme.</summary>
    [HttpDelete("one-offs/{id}")]
    [Authorize]
    public async Task<ActionResult> RemoveOneOff([FromRoute] Guid id)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        await _store.UpdateAsync(d => d.OneOffs.RemoveAll(o => o.Id == id)).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Unfreezes the current and upcoming airings so today is regenerated.</summary>
    [HttpPost("regenerate")]
    [Authorize]
    public async Task<ActionResult> Regenerate()
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        await _store.UpdateAsync(UnfreezeFuture).ConfigureAwait(false);
        return NoContent();
    }

    // ---------------------------------------------------------------- recordings

    /// <summary>Lists recordings (programmes deferred to watch later).</summary>
    [HttpGet("recordings")]
    [Authorize]
    public ActionResult<List<RecordingStatus>> GetRecordings()
    {
        var data = _store.Get();
        var user = _engine.ResolveHouseholdUser(data);
        var list = new List<RecordingStatus>();
        foreach (var r in data.Recordings.OrderByDescending(r => r.RecordedAt))
        {
            var status = new RecordingStatus { Recording = r };
            var item = _catalog.GetItem(r.ItemId);
            if (item is null || user is null)
            {
                status.Missing = true;
            }
            else
            {
                status.Item = _engine.Summarize(item);
                var ud = _catalog.GetUserData(user, item);
                status.IsWatched = ud?.Played == true;
                status.PositionTicks = ud?.PlaybackPositionTicks ?? 0;
                if (item is Episode ep)
                {
                    status.SeriesName = ep.SeriesName;
                    status.SeriesId = ep.SeriesId;
                    status.Season = ep.ParentIndexNumber;
                    status.Episode = ep.IndexNumber;
                }
            }

            list.Add(status);
        }

        return list;
    }

    /// <summary>Records (defers) a programme so the schedule moves on without it.</summary>
    [HttpPost("recordings")]
    [Authorize]
    public async Task<ActionResult<Recording>> AddRecording([FromBody] RecordRequest request)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var item = _catalog.GetItem(request.ItemId);
        if (item is null)
        {
            return NotFound(new { Message = "Item not found" });
        }

        var rec = await _store.UpdateAsync(
            d =>
            {
                var existing = d.Recordings.FirstOrDefault(r => r.ItemId == item.Id);
                if (existing is not null)
                {
                    return existing;
                }

                var r = new Recording
                {
                    ItemId = item.Id,
                    LineupEntryId = request.LineupEntryId,
                    AiringStart = request.AiringStart,
                    Title = item is Episode ep ? ep.SeriesName ?? item.Name ?? string.Empty : item.Name ?? string.Empty,
                    Subtitle = item is Episode ep2 ? ScheduleEngine.EpisodeCode(ep2.ParentIndexNumber, ep2.IndexNumber) + " · " + ep2.Name : (item.ProductionYear?.ToString(CultureInfo.InvariantCulture))
                };
                d.Recordings.Add(r);

                // Regenerate the rest of today so the slot can be re-used.
                UnfreezeFuture(d);
                return r;
            }).ConfigureAwait(false);
        return rec;
    }

    /// <summary>Removes a recording (the programme returns to the schedule if unwatched).</summary>
    [HttpDelete("recordings/{id}")]
    [Authorize]
    public async Task<ActionResult> RemoveRecording([FromRoute] Guid id)
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        await _store.UpdateAsync(d => d.Recordings.RemoveAll(r => r.Id == id)).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Clears watched recordings.</summary>
    [HttpDelete("recordings/watched")]
    [Authorize]
    public async Task<ActionResult> ClearWatchedRecordings()
    {
        if (!await EnsureCanEditAsync().ConfigureAwait(false))
        {
            return Forbid();
        }

        var data = _store.Get();
        var user = _engine.ResolveHouseholdUser(data);
        if (user is null)
        {
            return NoContent();
        }

        var watched = data.Recordings.Where(r =>
        {
            var item = _catalog.GetItem(r.ItemId);
            return item is null || _catalog.IsWatched(user, item);
        }).Select(r => r.Id).ToHashSet();
        await _store.UpdateAsync(d => d.Recordings.RemoveAll(r => watched.Contains(r.Id))).ConfigureAwait(false);
        return NoContent();
    }

    // ---------------------------------------------------------------- play state

    /// <summary>Updates the household user's play state for an item (used when the viewer is logged in as somebody else).</summary>
    [HttpPost("playstate")]
    [Authorize]
    public async Task<ActionResult> PostPlayState([FromBody] PlayStateRequest request)
    {
        var auth = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        var data = _store.Get();
        var user = _engine.ResolveHouseholdUser(data);
        var item = _catalog.GetItem(request.ItemId);
        if (user is null || item is null)
        {
            return NotFound();
        }

        if (!CanEdit(data, auth))
        {
            return Forbid();
        }

        try
        {
            _catalog.SavePlayState(user, item, request.PositionTicks, request.Played);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jelly Schedule: could not save play state");
            return StatusCode(StatusCodes.Status500InternalServerError, new { ex.Message });
        }

        return NoContent();
    }

    // ---------------------------------------------------------------- integrations

    /// <summary>Gets movies Radarr is waiting for.</summary>
    [HttpGet("radarr/upcoming")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<UpcomingMovie>>> GetRadarrUpcoming([FromQuery] bool refresh = false, CancellationToken cancellationToken = default)
        => (await _airings.GetRadarrUpcomingAsync(refresh, cancellationToken).ConfigureAwait(false)).ToList();

    /// <summary>Tests an integration. When url/apiKey are omitted, the saved configuration is used.</summary>
    [HttpGet("integrations/test")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult<IntegrationStatus>> TestIntegration([FromQuery] string service, [FromQuery] string? url, [FromQuery] string? apiKey, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (string.Equals(service, "sonarr", StringComparison.OrdinalIgnoreCase))
        {
            url ??= config?.SonarrUrl;
            apiKey ??= config?.SonarrApiKey;
        }
        else if (string.Equals(service, "radarr", StringComparison.OrdinalIgnoreCase))
        {
            url ??= config?.RadarrUrl;
            apiKey ??= config?.RadarrApiKey;
        }

        return await _airings.TestAsync(service, url, apiKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears cached airing information.</summary>
    [HttpPost("integrations/refresh")]
    [Authorize]
    public async Task<ActionResult> RefreshIntegrations(CancellationToken cancellationToken)
    {
        _airings.Invalidate();
        await _engine.GetLineupStatusAsync(true, true, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    // ---------------------------------------------------------------- calendar

    /// <summary>iCalendar feed of the schedule.</summary>
    [HttpGet("calendar.ics")]
    [AllowAnonymous]
    [Produces("text/calendar")]
    public async Task<ActionResult> GetCalendar([FromQuery] string? key, [FromQuery] int days = 21, CancellationToken cancellationToken = default)
    {
        var data = _store.Get();
        if (string.IsNullOrEmpty(key) || !string.Equals(key, data.Settings.CalendarKey, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        var ics = await _engine.BuildCalendarAsync(Math.Clamp(days, 1, 60), cancellationToken).ConfigureAwait(false);
        return Content(ics, "text/calendar; charset=utf-8");
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsAdmin(AuthorizationInfo auth) => auth.User?.HasPermission(PermissionKind.IsAdministrator) == true;

    private static bool CanEdit(ScheduleData data, AuthorizationInfo auth) => auth.IsAuthenticated && (!data.Settings.AdminOnlyEditing || IsAdmin(auth));

    private static List<DayOfWeek> Normalize(IEnumerable<DayOfWeek>? days)
        => days is null ? [] : days.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();

    private static void UnfreezeFuture(ScheduleData d)
    {
        var now = DateTimeOffset.UtcNow;
        d.Frozen.RemoveAll(a => a.End > now);
    }

    private static string MimeFor(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            ".webmanifest" => "application/manifest+json",
            _ => "application/octet-stream"
        };
    }

    private async Task<bool> EnsureCanEditAsync()
    {
        var auth = await _auth.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        return CanEdit(_store.Get(), auth);
    }

    private static string? ReadEmbedded(string file)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web." + file);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? BuildAppHtml()
    {
        var html = ReadEmbedded("app.html");
        var css = ReadEmbedded("app.css");
        var jsCode = ReadEmbedded("app.js");
        if (html is null || css is null || jsCode is null)
        {
            return null;
        }

        return html
            .Replace("/*{{CSS}}*/", css, StringComparison.Ordinal)
            .Replace("/*{{JS}}*/", jsCode.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase), StringComparison.Ordinal);
    }

    private ActionResult ServeEmbedded(string file)
    {
        if (string.IsNullOrEmpty(file) || file.Contains("..", StringComparison.Ordinal) || file.Contains('/', StringComparison.Ordinal))
        {
            return NotFound();
        }

        var assembly = typeof(Plugin).Assembly;
        var resourceName = typeof(Plugin).Namespace + ".Web." + file;
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, MimeFor(file));
    }
}
