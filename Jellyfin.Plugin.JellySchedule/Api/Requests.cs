using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySchedule.Models;

namespace Jellyfin.Plugin.JellySchedule.Api;

public class AddLineupRequest
{
    public Guid ItemId { get; set; }

    public PlayMode Mode { get; set; } = PlayMode.InOrder;

    public List<DayOfWeek> Days { get; set; } = [];

    public int EpisodesPerAiring { get; set; } = 1;

    public string? StartFrom { get; set; }
}

public class UpdateLineupRequest
{
    public PlayMode? Mode { get; set; }

    public List<DayOfWeek>? Days { get; set; }

    public int? EpisodesPerAiring { get; set; }

    public string? StartFrom { get; set; }

    public bool? Paused { get; set; }

    public bool ClearStartFrom { get; set; }
}

public class ReorderRequest
{
    public List<Guid> Ids { get; set; } = [];
}

public class RecordRequest
{
    public Guid ItemId { get; set; }

    public DateTimeOffset? AiringStart { get; set; }

    public Guid? LineupEntryId { get; set; }
}

public class PlayStateRequest
{
    public Guid ItemId { get; set; }

    public long? PositionTicks { get; set; }

    public bool? Played { get; set; }
}

public class OneOffRequest
{
    public DateOnly Date { get; set; }

    public string Start { get; set; } = "20:00";

    public Guid ItemId { get; set; }

    public int? DurationMinutes { get; set; }
}

public class UserInfo
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}

public class StateResponse
{
    public string Version { get; set; } = string.Empty;

    public UserInfo Me { get; set; } = new();

    public UserInfo? HouseholdUser { get; set; }

    public List<UserInfo> Users { get; set; } = [];

    public bool CanEdit { get; set; }

    public ScheduleSettings Settings { get; set; } = new();

    public List<ViewingWindow> Windows { get; set; } = [];

    public List<LineupEntry> Lineup { get; set; } = [];

    public MovieNightSettings MovieNight { get; set; } = new();

    public List<Blackout> Blackouts { get; set; } = [];

    public List<OneOff> OneOffs { get; set; } = [];

    public Dictionary<string, bool> Integrations { get; set; } = [];

    public string ServerTimeZone { get; set; } = string.Empty;

    public DateTimeOffset Now { get; set; }
}

public class RecordingStatus
{
    public Recording Recording { get; set; } = new();

    public ItemSummary? Item { get; set; }

    public string? SeriesName { get; set; }

    public Guid? SeriesId { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public bool IsWatched { get; set; }

    public long PositionTicks { get; set; }

    public bool Missing { get; set; }
}

public class EpisodeInfo
{
    public Guid Id { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Watched { get; set; }

    public int RuntimeMinutes { get; set; }
}
