using Jellyfin.Plugin.JellySchedule.Models;
using Jellyfin.Plugin.JellySchedule.Services;
using JellySchedule.DevHost;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.JellySchedule.Tests;

public sealed class ScheduleEngineTests : IDisposable
{
    // Saturday 19 Sep 2026, 10:00 UTC.
    private static readonly DateTimeOffset _now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jellyschedule-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCatalog _catalog = new();
    private readonly ScheduleStore _store;
    private readonly ScheduleEngine _engine;
    private DateTimeOffset _clock = _now;

    public ScheduleEngineTests()
    {
        _store = new ScheduleStore(new TestPaths(_dir), NullLogger<ScheduleStore>.Instance);
        var airings = new AiringInfoService(new NoHttpClientFactory(), NullLogger<AiringInfoService>.Instance);
        _engine = new ScheduleEngine(_store, _catalog, airings, NullLogger<ScheduleEngine>.Instance) { UtcNow = () => _clock };
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Places_pinned_show_movie_and_reruns_in_slots()
    {
        await SeedAsync();
        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 7, false, CancellationToken.None);

        var tue = guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 22));
        var first = tue.Airings[0];
        Assert.Equal("The Bear", first.SeriesName);
        Assert.Equal(1, first.Season);
        Assert.Equal(1, first.Episode);
        Assert.Equal("20:00", first.Start.ToString("HH:mm"));
        Assert.Equal(30, first.DurationMinutes);
        Assert.True(first.IsSeasonPremiere);

        // A 50-minute episode is rounded up to a 60-minute slot.
        var severance = tue.Airings.Single(a => a.SeriesName == "Severance");
        Assert.Equal(60, severance.DurationMinutes);
        Assert.Equal(50, severance.RuntimeMinutes);

        // Leftover time is filled with a re-run and nothing runs past 22:00.
        Assert.Contains(tue.Airings, a => a.Kind == AiringKind.ReRun);
        Assert.All(tue.Airings, a => Assert.True(a.End <= tue.Windows[0].End));

        // Sunday is movie night: the movie starts the evening and takes a 3-hour block.
        // The projection always starts from today (Sat 19th), so Sun 20th gets the first movie and Sun 27th the second.
        var sun = guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 27));
        var movie = sun.Airings.First();
        Assert.Equal(AiringKind.Movie, movie.Kind);
        Assert.Equal("Heat", movie.Title);
        Assert.Equal("19:00", movie.Start.ToString("HH:mm"));
        Assert.Equal(180, movie.DurationMinutes);
        Assert.False(movie.RunsOver);

        var thisWeek = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 19), 2, false, CancellationToken.None);
        Assert.Equal("Dune: Part Two", thisWeek.Days.Single(d => d.Date == new DateOnly(2026, 9, 20)).Airings.First().Title);
    }

    [Fact]
    public async Task Guide_is_deterministic_between_calls()
    {
        await SeedAsync();
        var a = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 14, false, CancellationToken.None);
        var b = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 14, false, CancellationToken.None);
        Assert.Equal(Flatten(a), Flatten(b));
        Assert.True(a.Days.SelectMany(d => d.Airings).Count(x => x.Kind == AiringKind.ReRun) >= 3);
    }

    [Fact]
    public async Task Watched_and_recorded_episodes_are_skipped()
    {
        await SeedAsync();
        _catalog.MarkWatched(_catalog.Episode("The Bear", 1, 1)!.Id);
        var ep2 = _catalog.Episode("The Bear", 1, 2)!;
        await _store.UpdateAsync(d => d.Recordings.Add(new Recording { ItemId = ep2.Id, Title = "The Bear" }));

        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 22), 1, false, CancellationToken.None);
        var bear = guide.Days[0].Airings.First(a => a.SeriesName == "The Bear");
        Assert.Equal(3, bear.Episode);

        var status = await _engine.GetLineupStatusAsync(false, false, CancellationToken.None);
        var s = status.Single(x => x.Entry.Name == "The Bear");
        Assert.Equal(1, s.Watched);
        Assert.Equal(1, s.Recorded);
        Assert.Equal("S01E03 · The Bear 1x03", s.NextLabel);
    }

    [Fact]
    public async Task Show_airs_once_per_week_across_weeks_and_advances()
    {
        await SeedAsync();
        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 21, false, CancellationToken.None);
        var bears = guide.Days.SelectMany(d => d.Airings).Where(a => a.SeriesName == "The Bear" && a.Kind == AiringKind.Episode).ToList();
        Assert.Equal(3, bears.Count);
        Assert.Equal([1, 2, 3], bears.Select(b => b.Episode!.Value).ToArray());
        Assert.All(bears, b => Assert.Equal(DayOfWeek.Tuesday, b.Start.DayOfWeek));
    }

    [Fact]
    public async Task Started_airings_are_frozen_and_missed_ones_air_again()
    {
        await SeedAsync(includeToday: true);

        // 10:00 on Saturday: the test window is 09:30-12:00, so something is on now and gets frozen.
        var live = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 19), 1, true, CancellationToken.None);
        Assert.NotNull(live.OnNow);
        var onNow = live.OnNow!;
        Assert.True(onNow.IsFrozen);
        Assert.Single(_store.Get().Frozen);

        // Lineup edits do not move what has already started.
        await _store.UpdateAsync(d => d.Lineup.First(e => e.Id == onNow.LineupEntryId).Order = 99);
        var again = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 19), 1, true, CancellationToken.None);
        Assert.Equal(onNow.Id, again.OnNow!.Id);

        // Later that day, unwatched: it is marked missed and airs again in the next slot for that show.
        _clock = _now.AddHours(4);
        var later = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 19), 8, true, CancellationToken.None);
        var missed = later.Days[0].Airings.Single(a => a.Id == onNow.Id);
        Assert.True(missed.IsMissed);
        var next = later.Days.Skip(1).SelectMany(d => d.Airings).First(a => a.LineupEntryId == onNow.LineupEntryId && a.Kind == AiringKind.Episode);
        Assert.Equal(onNow.ItemId, next.ItemId);
    }

    [Fact]
    public async Task Blackout_days_have_no_programmes()
    {
        await SeedAsync();
        await _store.UpdateAsync(d => d.Blackouts.Add(new Blackout { From = new DateOnly(2026, 9, 22), To = new DateOnly(2026, 9, 24), Label = "Away" }));
        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 7, false, CancellationToken.None);
        Assert.Empty(guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 22)).Airings);
        Assert.Empty(guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 24)).Airings);
        Assert.NotEmpty(guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 27)).Airings);
        // The pinned show simply resumes the following week with the same episode.
        var bear = guide.Days.SelectMany(d => d.Airings).Where(a => a.SeriesName == "The Bear").ToList();
        Assert.Empty(bear);
        var nextWeek = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 28), 7, false, CancellationToken.None);
        Assert.Equal(1, nextWeek.Days.SelectMany(d => d.Airings).First(a => a.SeriesName == "The Bear").Episode);
    }

    [Fact]
    public async Task One_offs_and_start_from_are_honoured()
    {
        await SeedAsync();
        var heat = _catalog.Movie("Heat")!;
        await _store.UpdateAsync(d =>
        {
            d.OneOffs.Add(new OneOff { Date = new DateOnly(2026, 9, 24), Start = "20:00", ItemId = heat.Id, Name = "Heat" });
            d.Lineup.First(e => e.Name == "Severance").StartFrom = "S02E01";
        });
        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 21), 7, false, CancellationToken.None);
        var thu = guide.Days.Single(d => d.Date == new DateOnly(2026, 9, 24));
        Assert.Equal(AiringKind.OneOff, thu.Airings[0].Kind);
        Assert.Equal("Heat", thu.Airings[0].Title);
        Assert.True(thu.Airings[0].RunsOver || thu.Airings[0].End > thu.Windows[0].End);
        var sev = guide.Days.SelectMany(d => d.Airings).First(a => a.SeriesName == "Severance");
        Assert.Equal(2, sev.Season);
        Assert.Equal(1, sev.Episode);
    }

    [Fact]
    public async Task Windows_past_midnight_and_calendar_export_work()
    {
        await SeedAsync();
        await _store.UpdateAsync(d => d.Windows.Add(new ViewingWindow { Days = [DayOfWeek.Friday], Start = "23:00", End = "01:00" }));
        var guide = await _engine.BuildGuideAsync(new DateOnly(2026, 9, 25), 1, false, CancellationToken.None);
        var fri = guide.Days[0];
        Assert.Single(fri.Windows);
        Assert.Equal(new DateOnly(2026, 9, 26), DateOnly.FromDateTime(fri.Windows[0].End.DateTime));
        Assert.NotEmpty(fri.Airings);
        Assert.All(fri.Airings, a => Assert.True(a.Start >= fri.Windows[0].Start && a.End <= fri.Windows[0].End));

        var ics = await _engine.BuildCalendarAsync(14, CancellationToken.None);
        Assert.Contains("BEGIN:VCALENDAR", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:The Bear S01E01", ics, StringComparison.Ordinal);
    }

    private static string Flatten(GuideResult g) => string.Join("|", g.Days.SelectMany(d => d.Airings).Select(a => a.Id + ":" + a.Kind));

    private async Task SeedAsync(bool includeToday = false)
    {
        await _store.UpdateAsync(d =>
        {
            d.Settings.HouseholdUserId = _catalog.HouseholdUser.Id;
            d.Settings.TimeZoneId = "Etc/UTC";
            d.Windows.Add(new ViewingWindow { Days = [DayOfWeek.Tuesday, DayOfWeek.Thursday], Start = "20:00", End = "22:00" });
            d.Windows.Add(new ViewingWindow { Days = [DayOfWeek.Sunday], Start = "19:00", End = "22:30", Label = "Sunday night" });
            if (includeToday)
            {
                d.Windows.Add(new ViewingWindow { Days = [DayOfWeek.Saturday], Start = "09:30", End = "12:00" });
            }

            var order = 0;
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Series("The Bear")!.Id, Kind = LineupKind.Series, Name = "The Bear", Days = [DayOfWeek.Tuesday], Order = order++ });
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Series("Severance")!.Id, Kind = LineupKind.Series, Name = "Severance", Order = order++ });
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Series("Slow Horses")!.Id, Kind = LineupKind.Series, Name = "Slow Horses", Order = order++ });
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Series("Seinfeld")!.Id, Kind = LineupKind.Series, Name = "Seinfeld", Mode = PlayMode.ReRun, Order = order++ });
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Series("Frasier")!.Id, Kind = LineupKind.Series, Name = "Frasier", Mode = PlayMode.ReRun, Order = order++ });
            d.Lineup.Add(new LineupEntry { ItemId = _catalog.Collection("Sunday Blockbusters")!.Id, Kind = LineupKind.Collection, Name = "Sunday Blockbusters", Order = order++ });
            d.MovieNight = new MovieNightSettings { Days = [DayOfWeek.Sunday], Position = MoviePosition.Start };
        });
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler { Proxy = null, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    }

    private sealed class TestPaths : IApplicationPaths
    {
        public TestPaths(string root)
        {
            ProgramDataPath = root;
            Directory.CreateDirectory(Path.Combine(root, "plugins", "configurations"));
        }

        public string ProgramDataPath { get; }

        public string WebPath => ProgramDataPath;

        public string ProgramSystemPath => ProgramDataPath;

        public string DataPath => ProgramDataPath;

        public string ImageCachePath => ProgramDataPath;

        public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");

        public string PluginConfigurationsPath => Path.Combine(ProgramDataPath, "plugins", "configurations");

        public string LogDirectoryPath => ProgramDataPath;

        public string ConfigurationDirectoryPath => ProgramDataPath;

        public string SystemConfigurationFilePath => Path.Combine(ProgramDataPath, "system.xml");

        public string CachePath => ProgramDataPath;

        public string TempDirectory => ProgramDataPath;

        public string VirtualDataPath => "%AppDataPath%";

        public string TrickplayPath => ProgramDataPath;

        public string BackupPath => ProgramDataPath;

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
        {
        }
    }
}
