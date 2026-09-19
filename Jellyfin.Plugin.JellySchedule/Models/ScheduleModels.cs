using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellySchedule.Models;

/// <summary>What kind of library item a lineup entry points at.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LineupKind
{
    /// <summary>A TV series.</summary>
    Series,

    /// <summary>A single movie.</summary>
    Movie,

    /// <summary>A collection (box set) of movies.</summary>
    Collection,

    /// <summary>A playlist of movies.</summary>
    Playlist
}

/// <summary>How a series is played.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayMode
{
    /// <summary>Play unwatched episodes in order (first-run).</summary>
    InOrder,

    /// <summary>Random episodes regardless of watched status (re-runs).</summary>
    ReRun
}

/// <summary>Where the movie is placed on a movie night.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MoviePosition
{
    /// <summary>At the start of the viewing window.</summary>
    Start,

    /// <summary>At the end of the viewing window.</summary>
    End
}

/// <summary>How the next movie is chosen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MovieOrder
{
    /// <summary>In the order they were added.</summary>
    AsAdded,

    /// <summary>Shuffled (stable for a given week).</summary>
    Shuffle
}

/// <summary>What to do with time left over after first-run shows are placed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FillMode
{
    /// <summary>Fill with re-run shows.</summary>
    Reruns,

    /// <summary>Leave it off air.</summary>
    Nothing,

    /// <summary>Give first-run shows another episode, then re-runs.</summary>
    MoreEpisodes
}

/// <summary>How "tuning in" behaves.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LiveMode
{
    /// <summary>Programmes start from the beginning (or resume) whenever you tune in during the slot.</summary>
    Relaxed,

    /// <summary>Join in progress, like real broadcast TV.</summary>
    Strict
}

/// <summary>How a re-run show is chosen for a free slot.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RerunPicker
{
    /// <summary>Random (seeded so the guide is stable).</summary>
    Random,

    /// <summary>Rotate through the re-run shows.</summary>
    RoundRobin
}

/// <summary>Kind of airing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiringKind
{
    /// <summary>A first-run episode.</summary>
    Episode,

    /// <summary>A movie.</summary>
    Movie,

    /// <summary>A re-run episode.</summary>
    ReRun,

    /// <summary>A one-off placement.</summary>
    OneOff
}

/// <summary>The persisted schedule.</summary>
public class ScheduleData
{
    public int Version { get; set; } = 1;

    public ScheduleSettings Settings { get; set; } = new();

    public List<ViewingWindow> Windows { get; set; } = [];

    public List<LineupEntry> Lineup { get; set; } = [];

    public MovieNightSettings MovieNight { get; set; } = new();

    public List<Recording> Recordings { get; set; } = [];

    public List<Blackout> Blackouts { get; set; } = [];

    public List<OneOff> OneOffs { get; set; } = [];

    /// <summary>Airings that have started; kept so the guide's past and present are stable.</summary>
    public List<Airing> Frozen { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Household-wide settings.</summary>
public class ScheduleSettings
{
    /// <summary>The Jellyfin user whose watched status drives the schedule.</summary>
    public Guid HouseholdUserId { get; set; }

    /// <summary>IANA or Windows time zone id. Empty means the server's local zone.</summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>Slot granularity in minutes (30 or 60).</summary>
    public int SlotMinutes { get; set; } = 30;

    public FillMode FillMode { get; set; } = FillMode.Reruns;

    public LiveMode LiveMode { get; set; } = LiveMode.Relaxed;

    public bool AutoplayOnOpen { get; set; } = true;

    public bool IncludeSpecials { get; set; }

    public RerunPicker RerunPicker { get; set; } = RerunPicker.Random;

    /// <summary>When true only administrators may change the lineup and schedule.</summary>
    public bool AdminOnlyEditing { get; set; }

    /// <summary>Secret used for the iCal feed URL.</summary>
    public string CalendarKey { get; set; } = string.Empty;

    /// <summary>How many minutes after a programme's scheduled start it can still be started from the beginning in relaxed mode. 0 = whole slot.</summary>
    public int JoinGraceMinutes { get; set; }
}

/// <summary>A weekly viewing window, e.g. Mon+Wed 20:00-22:00.</summary>
public class ViewingWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public List<DayOfWeek> Days { get; set; } = [];

    /// <summary>Start time as HH:mm.</summary>
    public string Start { get; set; } = "20:00";

    /// <summary>End time as HH:mm. An end at or before the start means it runs past midnight.</summary>
    public string End { get; set; } = "22:00";

    public string? Label { get; set; }
}

/// <summary>A show or movie source in the lineup.</summary>
public class LineupEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemId { get; set; }

    public LineupKind Kind { get; set; }

    public PlayMode Mode { get; set; } = PlayMode.InOrder;

    /// <summary>Cached display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Days this entry may air on. Empty means any viewing day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    public int EpisodesPerAiring { get; set; } = 1;

    public int Order { get; set; }

    public bool Paused { get; set; }

    /// <summary>Optional starting point such as "S03E01"; earlier episodes are treated as already seen.</summary>
    public string? StartFrom { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Movie night settings.</summary>
public class MovieNightSettings
{
    public List<DayOfWeek> Days { get; set; } = [];

    public MoviePosition Position { get; set; } = MoviePosition.Start;

    public MovieOrder Order { get; set; } = MovieOrder.AsAdded;

    /// <summary>Optional fixed start time (HH:mm). When set, overrides the position.</summary>
    public string? StartTime { get; set; }
}

/// <summary>A programme deferred to watch later ("recorded").</summary>
public class Recording
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemId { get; set; }

    public Guid? LineupEntryId { get; set; }

    public DateTimeOffset? AiringStart { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }
}

/// <summary>A date range with no programming (holidays, away).</summary>
public class Blackout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public string Label { get; set; } = "Away";
}

/// <summary>A one-off placement of a specific item at a specific time.</summary>
public class OneOff
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly Date { get; set; }

    /// <summary>Start time as HH:mm.</summary>
    public string Start { get; set; } = "20:00";

    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? DurationMinutes { get; set; }
}

/// <summary>A programme in the guide.</summary>
public class Airing
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    /// <summary>Slot length in minutes.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Actual runtime in minutes.</summary>
    public int RuntimeMinutes { get; set; }

    public AiringKind Kind { get; set; }

    public Guid ItemId { get; set; }

    public Guid? SeriesId { get; set; }

    public Guid? LineupEntryId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? SeriesName { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string? Overview { get; set; }

    public int? Year { get; set; }

    public string? OfficialRating { get; set; }

    public float? CommunityRating { get; set; }

    public bool HasPrimaryImage { get; set; }

    public bool SeriesHasPrimaryImage { get; set; }

    public bool HasBackdrop { get; set; }

    public bool IsSeasonPremiere { get; set; }

    public bool IsSeasonFinale { get; set; }

    public bool IsSeriesFinale { get; set; }

    /// <summary>True when the programme runs past the end of its viewing window.</summary>
    public bool RunsOver { get; set; }

    // Live state (not persisted meaningfully; recomputed on every read)
    public bool IsWatched { get; set; }

    public long PositionTicks { get; set; }

    public bool IsRecorded { get; set; }

    public Guid? RecordingId { get; set; }

    public bool IsMissed { get; set; }

    public bool IsOnNow { get; set; }

    public bool IsFrozen { get; set; }
}
