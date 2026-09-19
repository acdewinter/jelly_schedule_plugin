using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellySchedule.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.Services;

/// <summary>
/// Builds the broadcast schedule from the viewing windows, the lineup and the household's watched status.
/// </summary>
public sealed class ScheduleEngine
{
    private const int MaxDays = 62;
    private const int FrozenRetentionDays = 60;

    private readonly ScheduleStore _store;
    private readonly ILibraryCatalog _catalog;
    private readonly AiringInfoService _airings;
    private readonly ILogger<ScheduleEngine> _logger;

    public ScheduleEngine(ScheduleStore store, ILibraryCatalog catalog, AiringInfoService airings, ILogger<ScheduleEngine> logger)
    {
        _store = store;
        _catalog = catalog;
        _airings = airings;
        _logger = logger;
    }

    /// <summary>Gets or sets the clock (overridable for tests).</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    public static DateTimeOffset NowIn(TimeZoneInfo tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);

    public static DateOnly TodayIn(TimeZoneInfo tz) => DateOnly.FromDateTime(NowIn(tz).DateTime);

    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(text ?? string.Empty, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    public static (int Season, int Episode)? ParseEpisodeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var m = System.Text.RegularExpressions.Regex.Match(code.Trim(), "^[Ss]?(\\d{1,3})[EeXx](\\d{1,4})$");
        if (!m.Success)
        {
            return null;
        }

        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Resolves the household user, falling back to the first administrator.</summary>
    public User? ResolveHouseholdUser(ScheduleData data)
    {
        var user = _catalog.GetUser(data.Settings.HouseholdUserId);
        if (user is not null)
        {
            return user;
        }

        return _catalog.GetUsers().FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator)) ?? _catalog.GetUsers().FirstOrDefault();
    }

    public static DateTimeOffset At(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public static string EpisodeCode(int? season, int? episode)
    {
        if (!season.HasValue && !episode.HasValue)
        {
            return string.Empty;
        }

        return string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", season ?? 0, episode ?? 0);
    }

    public ItemSummary Summarize(BaseItem item)
    {
        return new ItemSummary
        {
            Id = item.Id,
            Name = item.Name ?? string.Empty,
            Type = item.GetBaseItemKind().ToString(),
            Year = item.ProductionYear,
            Overview = item.Overview,
            HasPrimaryImage = item.HasImage(ImageType.Primary),
            HasBackdrop = item.HasImage(ImageType.Backdrop),
            Status = (item as Series)?.Status?.ToString(),
            RuntimeMinutes = LibraryCatalog.RuntimeMinutes(item, 0),
            OfficialRating = item.OfficialRating,
            CommunityRating = item.CommunityRating
        };
    }

    /// <summary>Builds the guide for a date range, freezing today's started airings so they stay put.</summary>
    public async Task<GuideResult> BuildGuideAsync(DateOnly from, int days, bool freeze, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var data = _store.Get();
        var settings = data.Settings;
        var tz = ResolveTimeZone(settings.TimeZoneId);
        var now = TimeZoneInfo.ConvertTime(UtcNow(), tz);
        var today = DateOnly.FromDateTime(now.DateTime);
        var slot = settings.SlotMinutes is 15 or 30 or 60 ? settings.SlotMinutes : 30;
        var to = from.AddDays(days - 1);

        var result = new GuideResult
        {
            Now = now,
            TimeZone = tz.Id,
            From = from,
            To = to,
            SlotMinutes = slot
        };

        var user = ResolveHouseholdUser(data);
        if (user is null)
        {
            result.Warning = "No household user could be resolved.";
            return result;
        }

        var ctx = new PlanContext(data, settings, tz, now, today, slot, user);
        LoadContext(ctx);

        var planStart = from < today ? from : today;
        var planEnd = to > today.AddDays(7) ? to : today.AddDays(7);
        if (planStart > today)
        {
            planStart = today;
        }

        var allDays = new List<GuideDay>();
        for (var d = planStart; d <= planEnd; d = d.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            allDays.Add(PlanDay(ctx, d));
        }

        if (freeze)
        {
            await FreezeAsync(ctx, allDays.First(x => x.Date == today), cancellationToken).ConfigureAwait(false);
        }

        // Live markers
        var flat = allDays.SelectMany(d => d.Airings).OrderBy(a => a.Start).ToList();
        result.OnNow = flat.FirstOrDefault(a => a.Start <= now && a.End > now);
        result.UpNext = flat.FirstOrDefault(a => a.Start > now);
        result.NextWindowStart = allDays.Where(d => d.Date >= today).SelectMany(d => d.Windows).Select(w => w.Start).Where(s => s > now).OrderBy(s => s).Cast<DateTimeOffset?>().FirstOrDefault();

        result.Days = allDays.Where(d => d.Date >= from && d.Date <= to).ToList();
        var visible = result.Days.SelectMany(d => d.Airings).ToList();
        result.Stats = new WeekStats
        {
            Programmes = visible.Count,
            Episodes = visible.Count(a => a.Kind == AiringKind.Episode),
            Movies = visible.Count(a => a.Kind == AiringKind.Movie),
            Reruns = visible.Count(a => a.Kind == AiringKind.ReRun),
            ScheduledMinutes = visible.Sum(a => a.DurationMinutes),
            Watched = visible.Count(a => a.IsWatched),
            Missed = visible.Count(a => a.IsMissed)
        };

        var spans = result.Days.SelectMany(d => d.Windows).ToList();
        if (spans.Count > 0)
        {
            result.EarliestStart = spans.Min(w => w.Start.TimeOfDay).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            var latest = spans.Max(w => (w.End - w.Start.Date.ToDateTimeOffsetAt(w.Start.Offset)).TotalMinutes);
            result.LatestEnd = TimeSpan.FromMinutes(latest).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        result.ComingUp = BuildComingUp(ctx, from, to.AddDays(1));
        if (data.Lineup.Count == 0 && data.Windows.Count == 0)
        {
            result.Warning = "empty";
        }

        return result;
    }

    /// <summary>Gets the lineup with live status for the household user.</summary>
    public async Task<List<LineupEntryStatus>> GetLineupStatusAsync(bool fetchAirings, bool forceRefresh, CancellationToken cancellationToken)
    {
        var data = _store.Get();
        var user = ResolveHouseholdUser(data);
        var result = new List<LineupEntryStatus>();
        var recordings = data.Recordings.Select(r => r.ItemId).ToHashSet();
        var tasks = new List<Task>();

        foreach (var entry in data.Lineup.OrderBy(e => e.Order))
        {
            var status = new LineupEntryStatus { Entry = entry };
            result.Add(status);
            var item = _catalog.GetItem(entry.ItemId);
            if (item is null || user is null)
            {
                status.Missing = true;
                continue;
            }

            status.Item = Summarize(item);
            if (item is Series series)
            {
                var episodes = _catalog.GetEpisodes(series, user, data.Settings.IncludeSpecials);
                var unwatched = _catalog.GetUnwatchedEpisodeIds(series, user);
                var startIndex = StartIndex(episodes, entry.StartFrom);
                status.Total = episodes.Count;
                status.Watched = episodes.Count(e => !unwatched.Contains(e.Id));
                status.Recorded = episodes.Count(e => recordings.Contains(e.Id));
                var next = episodes.Skip(startIndex).FirstOrDefault(e => unwatched.Contains(e.Id) && !recordings.Contains(e.Id));
                status.Remaining = episodes.Skip(startIndex).Count(e => unwatched.Contains(e.Id) && !recordings.Contains(e.Id));
                status.CaughtUp = next is null;
                if (next is not null)
                {
                    status.Next = Summarize(next);
                    status.NextLabel = EpisodeCode(next.ParentIndexNumber, next.IndexNumber) + (string.IsNullOrEmpty(next.Name) ? string.Empty : " · " + next.Name);
                }

                if (fetchAirings && entry.Mode == PlayMode.InOrder)
                {
                    var s = status;
                    tasks.Add(Task.Run(
                        async () =>
                        {
                            try
                            {
                                s.Airing = await _airings.GetForSeriesAsync(series, forceRefresh, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Jelly Schedule: airing lookup failed");
                            }
                        },
                        cancellationToken));
                }
                else
                {
                    status.Airing = _airings.Peek(series.Id);
                }
            }
            else
            {
                var movies = _catalog.ExpandMovies(entry, user);
                var unwatched = _catalog.GetUnwatchedIds(movies.Select(m => m.Id).ToList(), user);
                status.Total = movies.Count;
                status.Watched = movies.Count(m => !unwatched.Contains(m.Id));
                status.Recorded = movies.Count(m => recordings.Contains(m.Id));
                status.Remaining = movies.Count(m => unwatched.Contains(m.Id) && !recordings.Contains(m.Id));
                status.CaughtUp = status.Remaining == 0;
                var next = movies.FirstOrDefault(m => unwatched.Contains(m.Id) && !recordings.Contains(m.Id));
                if (next is not null)
                {
                    status.Next = Summarize(next);
                    status.NextLabel = next.Name + (next.ProductionYear.HasValue ? $" ({next.ProductionYear})" : string.Empty);
                }

                if (entry.Kind != LineupKind.Movie)
                {
                    status.Movies = movies.Take(200).Select(m =>
                    {
                        var s = Summarize(m);
                        s.Status = unwatched.Contains(m.Id) ? (recordings.Contains(m.Id) ? "Recorded" : "Unwatched") : "Watched";
                        return s;
                    }).ToList();
                }
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("Jelly Schedule: airing lookups timed out; returning cached values");
            }
        }

        return result;
    }

    /// <summary>Builds an iCalendar feed for the schedule.</summary>
    public async Task<string> BuildCalendarAsync(int days, CancellationToken cancellationToken)
    {
        var data = _store.Get();
        var tz = ResolveTimeZone(data.Settings.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow(), tz).DateTime);
        var guide = await BuildGuideAsync(today.AddDays(-7), days + 7, false, cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Jelly Schedule//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Jelly Schedule\r\n");
        foreach (var airing in guide.Days.SelectMany(d => d.Airings))
        {
            var title = airing.Kind == AiringKind.Movie ? airing.Title : $"{airing.SeriesName} {EpisodeCode(airing.Season, airing.Episode)}".Trim();
            if (airing.Kind == AiringKind.ReRun)
            {
                title += " (re-run)";
            }

            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append("UID:").Append(airing.Id).Append("@jellyschedule\r\n");
            sb.Append("DTSTAMP:").Append(DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("DTSTART:").Append(airing.Start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("DTEND:").Append(airing.End.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("SUMMARY:").Append(IcsEscape(title)).Append("\r\n");
            var desc = airing.Kind == AiringKind.Movie ? airing.Overview : airing.Title + (string.IsNullOrEmpty(airing.Overview) ? string.Empty : " — " + airing.Overview);
            if (!string.IsNullOrEmpty(desc))
            {
                sb.Append("DESCRIPTION:").Append(IcsEscape(desc)).Append("\r\n");
            }

            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string IcsEscape(string text)
    {
        var s = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return s.Length > 900 ? s[..900] + "…" : s;
    }

    private static int StartIndex(IReadOnlyList<Episode> episodes, string? startFrom)
    {
        var code = ParseEpisodeCode(startFrom);
        if (code is null)
        {
            return 0;
        }

        for (var i = 0; i < episodes.Count; i++)
        {
            var e = episodes[i];
            var s = e.ParentIndexNumber ?? 0;
            var n = e.IndexNumber ?? 0;
            if (s > code.Value.Season || (s == code.Value.Season && n >= code.Value.Episode))
            {
                return i;
            }
        }

        return episodes.Count;
    }

    private static int BlocksFor(int runtimeMinutes, int slot) => Math.Max(1, (int)Math.Ceiling(runtimeMinutes / (double)slot));

    private static int StableHash(params object[] parts)
    {
        unchecked
        {
            // FNV-1a over the textual parts, then a murmur3-style finalizer so small changes spread across all bits.
            uint h = 2166136261;
            foreach (var p in parts)
            {
                var s = p switch
                {
                    Guid g => g.ToString("N"),
                    DateOnly d => d.DayNumber.ToString(CultureInfo.InvariantCulture),
                    _ => Convert.ToString(p, CultureInfo.InvariantCulture) ?? string.Empty
                };
                foreach (var c in s)
                {
                    h ^= c;
                    h *= 16777619;
                }

                h ^= '|';
                h *= 16777619;
            }

            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (int)(h & 0x7fffffff);
        }
    }

    private static string AiringId(DateTimeOffset start, Guid itemId)
        => start.UtcTicks.ToString(CultureInfo.InvariantCulture) + "-" + itemId.ToString("N");

    private static bool InBlackout(ScheduleData data, DateOnly d, out Blackout? blackout)
    {
        blackout = data.Blackouts.FirstOrDefault(b => b.From <= d && b.To >= d);
        return blackout is not null;
    }

    private void LoadContext(PlanContext ctx)
    {
        var data = ctx.Data;
        ctx.RecordedItems = data.Recordings.Select(r => r.ItemId).ToHashSet();
        ctx.RecordingByItem = data.Recordings.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in data.Lineup.Where(e => !e.Paused).OrderBy(e => e.Order))
        {
            if (entry.Kind == LineupKind.Series)
            {
                if (_catalog.GetItem(entry.ItemId) is not Series series)
                {
                    continue;
                }

                var episodes = _catalog.GetEpisodes(series, ctx.User, ctx.Settings.IncludeSpecials);
                if (episodes.Count == 0)
                {
                    continue;
                }

                var sctx = new SeriesContext
                {
                    Entry = entry,
                    Series = series,
                    Episodes = episodes,
                    TypicalRuntime = LibraryCatalog.TypicalEpisodeMinutes(episodes),
                    StartIndex = StartIndex(episodes, entry.StartFrom),
                    Unwatched = entry.Mode == PlayMode.InOrder ? _catalog.GetUnwatchedEpisodeIds(series, ctx.User) : []
                };
                var lastSeason = episodes.Max(e => e.ParentIndexNumber ?? 0);
                sctx.LastSeason = lastSeason;
                sctx.SeriesEnded = series.Status == SeriesStatus.Ended;
                ctx.SeriesContexts.Add(sctx);
            }
            else
            {
                var movies = _catalog.ExpandMovies(entry, ctx.User);
                foreach (var m in movies)
                {
                    if (ctx.MoviePool.All(x => x.Movie.Id != m.Id))
                    {
                        ctx.MoviePool.Add((entry, m));
                    }
                }
            }
        }

        ctx.UnwatchedMovies = _catalog.GetUnwatchedIds(ctx.MoviePool.Select(p => p.Movie.Id).ToList(), ctx.User);
    }

    private GuideDay PlanDay(PlanContext ctx, DateOnly date)
    {
        var day = new GuideDay
        {
            Date = date,
            DayName = date.DayOfWeek.ToString(),
            IsToday = date == ctx.Today,
            IsPast = date < ctx.Today
        };

        InBlackout(ctx.Data, date, out var blackout);
        day.Blackout = blackout;

        var frozen = ctx.Data.Frozen
            .Where(a => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.Start, ctx.Tz).DateTime) == date)
            .OrderBy(a => a.Start)
            .ToList();

        foreach (var w in ctx.Data.Windows.Where(w => w.Days.Contains(date.DayOfWeek)))
        {
            if (!TryParseTime(w.Start, out var start) || !TryParseTime(w.End, out var end))
            {
                continue;
            }

            var s = At(date, start, ctx.Tz);
            var e = At(date, end, ctx.Tz);
            if (e <= s)
            {
                e = e.AddDays(1);
            }

            day.Windows.Add(new WindowSpan { Start = s, End = e, Label = w.Label });
        }

        day.Windows = day.Windows.OrderBy(w => w.Start).ToList();

        // Past days: frozen history only.
        if (day.IsPast)
        {
            foreach (var a in frozen)
            {
                a.IsFrozen = true;
                Annotate(ctx, a);
                day.Airings.Add(a);
            }

            RegisterPlaced(ctx, day.Airings);
            return day;
        }

        var grid = new DayGrid(day.Windows, ctx.Slot);

        // Today: frozen airings are fixed.
        foreach (var a in frozen)
        {
            a.IsFrozen = true;
            Annotate(ctx, a);
            grid.Occupy(a.Start, a.End);
            day.Airings.Add(a);
        }

        RegisterPlaced(ctx, day.Airings);

        if (blackout is not null)
        {
            day.Airings = day.Airings.OrderBy(a => a.Start).ToList();
            return day;
        }

        // Blocks that already ended today are no longer fillable.
        if (day.IsToday)
        {
            grid.RetirePassed(ctx.Now);
        }

        PlaceOneOffs(ctx, date, grid, day);
        PlaceMovie(ctx, date, grid, day);
        PlaceSeries(ctx, date, grid, day);
        if (ctx.Settings.FillMode == FillMode.MoreEpisodes)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                if (!PlaceSeries(ctx, date, grid, day))
                {
                    break;
                }
            }
        }

        if (ctx.Settings.FillMode != FillMode.Nothing)
        {
            PlaceReruns(ctx, date, grid, day);
        }

        day.Airings = day.Airings.OrderBy(a => a.Start).ToList();
        return day;
    }

    private void RegisterPlaced(PlanContext ctx, IEnumerable<Airing> airings)
    {
        foreach (var a in airings)
        {
            if (a.Kind == AiringKind.ReRun)
            {
                continue;
            }

            // A missed first-run airing (ended, unwatched, not recorded) must air again, so it is not consumed.
            var consumed = a.IsWatched || a.IsRecorded || a.End > ctx.Now;
            if (consumed)
            {
                ctx.Placed.Add(a.ItemId);
            }

            if (a.LineupEntryId.HasValue)
            {
                var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.Start, ctx.Tz).DateTime);
                if (!ctx.LastAired.TryGetValue(a.LineupEntryId.Value, out var prev) || prev < d)
                {
                    ctx.LastAired[a.LineupEntryId.Value] = d;
                }
            }
        }
    }

    private void PlaceOneOffs(PlanContext ctx, DateOnly date, DayGrid grid, GuideDay day)
    {
        foreach (var one in ctx.Data.OneOffs.Where(o => o.Date == date))
        {
            if (!TryParseTime(one.Start, out var t))
            {
                continue;
            }

            var item = _catalog.GetItem(one.ItemId);
            if (item is null)
            {
                continue;
            }

            var start = At(date, t, ctx.Tz);
            if (day.IsToday && start.AddMinutes(one.DurationMinutes ?? LibraryCatalog.RuntimeMinutes(item, 60)) <= ctx.Now && day.Airings.All(a => a.ItemId != item.Id))
            {
                // Already over and never frozen: skip.
                continue;
            }

            if (day.Airings.Any(a => a.ItemId == item.Id && Math.Abs((a.Start - start).TotalMinutes) < ctx.Slot))
            {
                continue;
            }

            var runtime = one.DurationMinutes ?? LibraryCatalog.RuntimeMinutes(item, 60);
            var blocks = BlocksFor(runtime, ctx.Slot);
            var airing = MakeAiring(ctx, item, start, blocks, AiringKind.OneOff, null);
            grid.Occupy(airing.Start, airing.End);
            ctx.Placed.Add(item.Id);
            day.Airings.Add(airing);
        }
    }

    private void PlaceMovie(PlanContext ctx, DateOnly date, DayGrid grid, GuideDay day)
    {
        var mn = ctx.Data.MovieNight;
        if (!mn.Days.Contains(date.DayOfWeek) || ctx.MoviePool.Count == 0 || grid.Blocks.Count == 0)
        {
            return;
        }

        if (day.Airings.Any(a => a.Kind == AiringKind.Movie))
        {
            return;
        }

        var candidates = ctx.MoviePool
            .Where(p => ctx.UnwatchedMovies.Contains(p.Movie.Id) && !ctx.RecordedItems.Contains(p.Movie.Id) && !ctx.Placed.Contains(p.Movie.Id))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        if (mn.Order == MovieOrder.Shuffle)
        {
            var week = (date.DayNumber - (int)date.DayOfWeek) / 7;
            candidates = candidates.OrderBy(p => StableHash(p.Movie.Id, week)).ToList();
        }

        var (entry, movie) = candidates[0];
        var runtime = LibraryCatalog.RuntimeMinutes(movie, 120);
        var blocks = BlocksFor(runtime, ctx.Slot);

        int? startIndex = null;
        if (!string.IsNullOrEmpty(mn.StartTime) && TryParseTime(mn.StartTime, out var fixedStart))
        {
            var at = At(date, fixedStart, ctx.Tz);
            startIndex = grid.IndexAtOrAfter(at);
        }

        if (startIndex is null)
        {
            startIndex = mn.Position == MoviePosition.End
                ? grid.FindRunFromEnd(blocks)
                : grid.FindRun(blocks, allowOverflow: true);
        }

        if (startIndex is null)
        {
            return;
        }

        var airing = MakeAiring(ctx, movie, grid.Blocks[startIndex.Value].Start, blocks, AiringKind.Movie, entry.Id);
        airing.RunsOver = grid.RunsOver(startIndex.Value, blocks);
        grid.Occupy(airing.Start, airing.End);
        ctx.Placed.Add(movie.Id);
        ctx.LastAired[entry.Id] = date;
        day.Airings.Add(airing);
    }

    private bool PlaceSeries(PlanContext ctx, DateOnly date, DayGrid grid, GuideDay day)
    {
        if (grid.FreeCount == 0)
        {
            return false;
        }

        var candidates = ctx.SeriesContexts
            .Where(s => s.Entry.Mode == PlayMode.InOrder)
            .Where(s => s.Entry.Days.Count == 0 || s.Entry.Days.Contains(date.DayOfWeek))
            .ToList();

        var pinned = candidates.Where(s => s.Entry.Days.Count > 0).OrderBy(s => s.Entry.Order);
        var floating = candidates.Where(s => s.Entry.Days.Count == 0)
            .OrderBy(s => ctx.LastAired.TryGetValue(s.Entry.Id, out var d) ? d : DateOnly.MinValue)
            .ThenBy(s => s.Entry.Order);

        var placedAny = false;
        foreach (var sctx in pinned.Concat(floating))
        {
            var perAiring = Math.Clamp(sctx.Entry.EpisodesPerAiring, 1, 6);
            if (ctx.Settings.FillMode == FillMode.MoreEpisodes && ctx.LastAired.TryGetValue(sctx.Entry.Id, out var last) && last == date)
            {
                perAiring = 1; // extra passes add one episode at a time
            }

            for (var i = 0; i < perAiring; i++)
            {
                var episode = NextEpisode(ctx, sctx);
                if (episode is null)
                {
                    break;
                }

                var runtime = LibraryCatalog.RuntimeMinutes(episode, sctx.TypicalRuntime);
                var blocks = BlocksFor(runtime, ctx.Slot);
                var index = grid.FindRun(blocks, allowOverflow: false);
                if (index is null)
                {
                    break;
                }

                var airing = MakeAiring(ctx, episode, grid.Blocks[index.Value].Start, blocks, AiringKind.Episode, sctx.Entry.Id);
                DecorateEpisode(airing, episode, sctx);
                grid.Occupy(airing.Start, airing.End);
                ctx.Placed.Add(episode.Id);
                ctx.LastAired[sctx.Entry.Id] = date;
                day.Airings.Add(airing);
                placedAny = true;
            }
        }

        return placedAny;
    }

    private void PlaceReruns(PlanContext ctx, DateOnly date, DayGrid grid, GuideDay day)
    {
        var reruns = ctx.SeriesContexts
            .Where(s => s.Entry.Mode == PlayMode.ReRun)
            .Where(s => s.Entry.Days.Count == 0 || s.Entry.Days.Contains(date.DayOfWeek))
            .OrderBy(s => s.Entry.Order)
            .ToList();
        if (reruns.Count == 0)
        {
            return;
        }

        var usedToday = new HashSet<Guid>(day.Airings.Select(a => a.ItemId));
        var guard = 0;
        while (guard++ < 64)
        {
            var index = grid.FirstFree();
            if (index is null)
            {
                break;
            }

            var runLength = grid.RunLengthAt(index.Value);
            var pick = ctx.Settings.RerunPicker == RerunPicker.RoundRobin
                ? reruns[ctx.RerunCounter++ % reruns.Count]
                : reruns[StableHash(date, index.Value, "show") % reruns.Count];

            Episode? chosen = null;
            var chosenBlocks = 0;
            for (var attempt = 0; attempt < 6 && chosen is null; attempt++)
            {
                var candidate = pick.Episodes[StableHash(pick.Entry.Id, date, index.Value, attempt) % pick.Episodes.Count];
                var blocks = BlocksFor(LibraryCatalog.RuntimeMinutes(candidate, pick.TypicalRuntime), ctx.Slot);
                if (blocks <= runLength && !usedToday.Contains(candidate.Id))
                {
                    chosen = candidate;
                    chosenBlocks = blocks;
                }
            }

            if (chosen is null)
            {
                // Fall back to the shortest episode of any re-run show that fits.
                foreach (var alt in reruns)
                {
                    var shortest = alt.Episodes
                        .Where(e => !usedToday.Contains(e.Id))
                        .OrderBy(e => LibraryCatalog.RuntimeMinutes(e, alt.TypicalRuntime))
                        .FirstOrDefault();
                    if (shortest is null)
                    {
                        continue;
                    }

                    var blocks = BlocksFor(LibraryCatalog.RuntimeMinutes(shortest, alt.TypicalRuntime), ctx.Slot);
                    if (blocks <= runLength)
                    {
                        pick = alt;
                        chosen = shortest;
                        chosenBlocks = blocks;
                        break;
                    }
                }
            }

            if (chosen is null)
            {
                grid.Retire(index.Value, runLength);
                continue;
            }

            var airing = MakeAiring(ctx, chosen, grid.Blocks[index.Value].Start, chosenBlocks, AiringKind.ReRun, pick.Entry.Id);
            grid.Occupy(airing.Start, airing.End);
            usedToday.Add(chosen.Id);
            day.Airings.Add(airing);
        }
    }

    private static Episode? NextEpisode(PlanContext ctx, SeriesContext sctx)
    {
        for (var i = sctx.StartIndex; i < sctx.Episodes.Count; i++)
        {
            var e = sctx.Episodes[i];
            if (!sctx.Unwatched.Contains(e.Id) || ctx.RecordedItems.Contains(e.Id) || ctx.Placed.Contains(e.Id))
            {
                continue;
            }

            return e;
        }

        return null;
    }

    private static void DecorateEpisode(Airing airing, Episode episode, SeriesContext sctx)
    {
        var season = episode.ParentIndexNumber ?? 0;
        var inSeason = sctx.Episodes.Where(e => (e.ParentIndexNumber ?? 0) == season).ToList();
        var first = inSeason.FirstOrDefault();
        var last = inSeason.LastOrDefault();
        airing.IsSeasonPremiere = first is not null && first.Id == episode.Id && season > 0 && inSeason.Count > 1;
        airing.IsSeasonFinale = last is not null && last.Id == episode.Id && season > 0 && inSeason.Count > 1;
        airing.IsSeriesFinale = airing.IsSeasonFinale && season == sctx.LastSeason && sctx.SeriesEnded;
    }

    private Airing MakeAiring(PlanContext ctx, BaseItem item, DateTimeOffset start, int blocks, AiringKind kind, Guid? lineupEntryId)
    {
        var duration = blocks * ctx.Slot;
        var airing = new Airing
        {
            Id = AiringId(start, item.Id),
            Start = start,
            End = start.AddMinutes(duration),
            DurationMinutes = duration,
            RuntimeMinutes = LibraryCatalog.RuntimeMinutes(item, kind == AiringKind.Movie ? 120 : 30),
            Kind = kind,
            ItemId = item.Id,
            LineupEntryId = lineupEntryId,
            Title = item.Name ?? string.Empty,
            Overview = item.Overview,
            Year = item.ProductionYear,
            OfficialRating = item.OfficialRating,
            CommunityRating = item.CommunityRating,
            HasPrimaryImage = item.HasImage(ImageType.Primary),
            HasBackdrop = item.HasImage(ImageType.Backdrop)
        };

        if (item is Episode episode)
        {
            airing.SeriesId = episode.SeriesId;
            airing.SeriesName = episode.SeriesName;
            airing.Season = episode.ParentIndexNumber;
            airing.Episode = episode.IndexNumber;
            var series = _catalog.GetItem(episode.SeriesId) as Series;
            airing.SeriesHasPrimaryImage = series?.HasImage(ImageType.Primary) ?? false;
            airing.HasBackdrop = series?.HasImage(ImageType.Backdrop) ?? false;
            airing.OfficialRating ??= series?.OfficialRating;
        }

        Annotate(ctx, airing);
        return airing;
    }

    private void Annotate(PlanContext ctx, Airing airing)
    {
        var item = _catalog.GetItem(airing.ItemId);
        if (item is not null)
        {
            var ud = _catalog.GetUserData(ctx.User, item);
            airing.IsWatched = ud?.Played == true;
            airing.PositionTicks = ud?.PlaybackPositionTicks ?? 0;
            if (item is Episode episode && airing.SeriesId is null)
            {
                airing.SeriesId = episode.SeriesId;
                airing.SeriesName ??= episode.SeriesName;
            }
        }

        airing.IsRecorded = ctx.RecordingByItem.TryGetValue(airing.ItemId, out var rec);
        airing.RecordingId = rec?.Id;
        airing.IsOnNow = airing.Start <= ctx.Now && airing.End > ctx.Now;
        airing.IsMissed = airing.IsFrozen && airing.End <= ctx.Now && !airing.IsWatched && !airing.IsRecorded && airing.Kind != AiringKind.ReRun;
    }

    private async Task FreezeAsync(PlanContext ctx, GuideDay today, CancellationToken cancellationToken)
    {
        var toFreeze = today.Airings.Where(a => !a.IsFrozen && a.Start <= ctx.Now).ToList();
        var cutoff = ctx.Now.AddDays(-FrozenRetentionDays);
        var needsPrune = ctx.Data.Frozen.Any(a => a.Start < cutoff);
        if (toFreeze.Count == 0 && !needsPrune)
        {
            return;
        }

        await _store.UpdateAsync(
            d =>
            {
                d.Frozen.RemoveAll(a => a.Start < cutoff);
                foreach (var a in toFreeze)
                {
                    if (d.Frozen.Any(f => f.Id == a.Id))
                    {
                        continue;
                    }

                    a.IsFrozen = true;
                    d.Frozen.Add(a);
                }
            },
            cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Jelly Schedule: froze {Count} airing(s)", toFreeze.Count);
    }

    private List<ComingUpItem> BuildComingUp(PlanContext ctx, DateOnly from, DateOnly to)
    {
        var list = new List<ComingUpItem>();
        var fromDt = At(from, TimeOnly.MinValue, ctx.Tz);
        var toDt = At(to, TimeOnly.MinValue, ctx.Tz);
        foreach (var sctx in ctx.SeriesContexts.Where(s => s.Entry.Mode == PlayMode.InOrder))
        {
            var info = _airings.Peek(sctx.Series.Id);
            if (info is null)
            {
                var series = sctx.Series;
                _ = Task.Run(() => _airings.GetForSeriesAsync(series, false, CancellationToken.None), CancellationToken.None);
                continue;
            }

            foreach (var ep in info.Upcoming.Concat(info.AiredNotAvailable).Where(e => e.AirDate.HasValue && e.AirDate.Value >= fromDt && e.AirDate.Value < toDt))
            {
                list.Add(new ComingUpItem
                {
                    SeriesId = sctx.Series.Id,
                    LineupEntryId = sctx.Entry.Id,
                    SeriesName = sctx.Series.Name ?? string.Empty,
                    Season = ep.Season,
                    Episode = ep.Episode,
                    Title = ep.Title,
                    AirDate = ep.AirDate,
                    Network = info.Network,
                    Source = info.Source,
                    HasFile = ep.HasFile
                });
            }
        }

        return list.OrderBy(c => c.AirDate).ToList();
    }

    private sealed class PlanContext
    {
        public PlanContext(ScheduleData data, ScheduleSettings settings, TimeZoneInfo tz, DateTimeOffset now, DateOnly today, int slot, User user)
        {
            Data = data;
            Settings = settings;
            Tz = tz;
            Now = now;
            Today = today;
            Slot = slot;
            User = user;
        }

        public ScheduleData Data { get; }

        public ScheduleSettings Settings { get; }

        public TimeZoneInfo Tz { get; }

        public DateTimeOffset Now { get; }

        public DateOnly Today { get; }

        public int Slot { get; }

        public User User { get; }

        public List<SeriesContext> SeriesContexts { get; } = [];

        public List<(LineupEntry Entry, Movie Movie)> MoviePool { get; } = [];

        public HashSet<Guid> UnwatchedMovies { get; set; } = [];

        public HashSet<Guid> RecordedItems { get; set; } = [];

        public Dictionary<Guid, Recording> RecordingByItem { get; set; } = [];

        public HashSet<Guid> Placed { get; } = [];

        public Dictionary<Guid, DateOnly> LastAired { get; } = [];

        public int RerunCounter { get; set; }
    }

    private sealed class SeriesContext
    {
        public required LineupEntry Entry { get; init; }

        public required Series Series { get; init; }

        public required IReadOnlyList<Episode> Episodes { get; init; }

        public int TypicalRuntime { get; init; }

        public int StartIndex { get; init; }

        public HashSet<Guid> Unwatched { get; init; } = [];

        public int LastSeason { get; set; }

        public bool SeriesEnded { get; set; }
    }

    /// <summary>The day's slots.</summary>
    private sealed class DayGrid
    {
        private readonly int _slot;

        public DayGrid(List<WindowSpan> windows, int slot)
        {
            _slot = slot;
            var seen = new HashSet<DateTimeOffset>();
            foreach (var w in windows)
            {
                for (var t = w.Start; t < w.End; t = t.AddMinutes(slot))
                {
                    if (seen.Add(t))
                    {
                        Blocks.Add(new Block { Start = t, WindowEnd = w.End });
                    }
                }
            }

            Blocks.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        public List<Block> Blocks { get; } = [];

        public int FreeCount => Blocks.Count(b => b.State == BlockState.Free);

        public void Occupy(DateTimeOffset start, DateTimeOffset end)
        {
            foreach (var b in Blocks)
            {
                if (b.Start >= start && b.Start < end)
                {
                    b.State = BlockState.Occupied;
                }
            }
        }

        public void RetirePassed(DateTimeOffset now)
        {
            foreach (var b in Blocks)
            {
                if (b.State == BlockState.Free && b.Start.AddMinutes(_slot) <= now)
                {
                    b.State = BlockState.Retired;
                }
            }
        }

        public void Retire(int index, int count)
        {
            for (var i = index; i < index + count && i < Blocks.Count; i++)
            {
                if (Blocks[i].State == BlockState.Free)
                {
                    Blocks[i].State = BlockState.Retired;
                }
            }
        }

        public int? FirstFree()
        {
            for (var i = 0; i < Blocks.Count; i++)
            {
                if (Blocks[i].State == BlockState.Free)
                {
                    return i;
                }
            }

            return null;
        }

        public int? IndexAtOrAfter(DateTimeOffset at)
        {
            for (var i = 0; i < Blocks.Count; i++)
            {
                if (Blocks[i].State == BlockState.Free && Blocks[i].Start >= at)
                {
                    return i;
                }
            }

            return null;
        }

        /// <summary>Number of consecutive free, contiguous blocks starting at index.</summary>
        public int RunLengthAt(int index)
        {
            var n = 0;
            for (var i = index; i < Blocks.Count; i++)
            {
                if (Blocks[i].State != BlockState.Free)
                {
                    break;
                }

                if (i > index && Blocks[i].Start != Blocks[i - 1].Start.AddMinutes(_slot))
                {
                    break;
                }

                n++;
            }

            return n;
        }

        public int? FindRun(int blocks, bool allowOverflow)
        {
            for (var i = 0; i < Blocks.Count; i++)
            {
                if (Blocks[i].State != BlockState.Free)
                {
                    continue;
                }

                var run = RunLengthAt(i);
                if (run >= blocks)
                {
                    return i;
                }

                if (allowOverflow && i + run == Blocks.Count)
                {
                    return i;
                }
            }

            return null;
        }

        public int? FindRunFromEnd(int blocks)
        {
            for (var i = Blocks.Count - 1; i >= 0; i--)
            {
                if (Blocks[i].State != BlockState.Free)
                {
                    continue;
                }

                // Walk back to the start of this free run.
                var start = i;
                while (start > 0 && Blocks[start - 1].State == BlockState.Free && Blocks[start].Start == Blocks[start - 1].Start.AddMinutes(_slot))
                {
                    start--;
                }

                var run = i - start + 1;
                if (run >= blocks)
                {
                    return i - blocks + 1;
                }

                if (i == Blocks.Count - 1)
                {
                    return start; // the movie runs over the end of the last window
                }

                i = start;
            }

            return null;
        }

        public bool RunsOver(int index, int blocks)
        {
            var endIndex = index + blocks - 1;
            if (endIndex >= Blocks.Count)
            {
                return true;
            }

            return Blocks[index].Start.AddMinutes(blocks * _slot) > Blocks[index].WindowEnd;
        }

        public sealed class Block
        {
            public DateTimeOffset Start { get; init; }

            public DateTimeOffset WindowEnd { get; init; }

            public BlockState State { get; set; }
        }

        public enum BlockState
        {
            Free,
            Occupied,
            Retired
        }
    }
}

internal static class DateTimeOffsetExtensions
{
    public static DateTimeOffset ToDateTimeOffsetAt(this DateTime date, TimeSpan offset) => new(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), offset);
}
