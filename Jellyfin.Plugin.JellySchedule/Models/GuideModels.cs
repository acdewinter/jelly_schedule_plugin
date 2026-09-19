using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySchedule.Services;

namespace Jellyfin.Plugin.JellySchedule.Models;

/// <summary>A day in the guide.</summary>
public class GuideDay
{
    public DateOnly Date { get; set; }

    public string DayName { get; set; } = string.Empty;

    public bool IsToday { get; set; }

    public bool IsPast { get; set; }

    public Blackout? Blackout { get; set; }

    public List<WindowSpan> Windows { get; set; } = [];

    public List<Airing> Airings { get; set; } = [];
}

/// <summary>A viewing window rendered on a specific day.</summary>
public class WindowSpan
{
    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    public string? Label { get; set; }
}

/// <summary>Weekly stats.</summary>
public class WeekStats
{
    public int Programmes { get; set; }

    public int Episodes { get; set; }

    public int Movies { get; set; }

    public int Reruns { get; set; }

    public int ScheduledMinutes { get; set; }

    public int Watched { get; set; }

    public int Missed { get; set; }
}

/// <summary>An episode that will be broadcast soon but is not in the library yet.</summary>
public class ComingUpItem
{
    public Guid SeriesId { get; set; }

    public Guid LineupEntryId { get; set; }

    public string SeriesName { get; set; } = string.Empty;

    public int Season { get; set; }

    public int Episode { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset? AirDate { get; set; }

    public string? Network { get; set; }

    public string Source { get; set; } = string.Empty;

    public bool HasFile { get; set; }
}

/// <summary>The guide.</summary>
public class GuideResult
{
    public DateTimeOffset Now { get; set; }

    public string TimeZone { get; set; } = string.Empty;

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public int SlotMinutes { get; set; }

    public string? EarliestStart { get; set; }

    public string? LatestEnd { get; set; }

    public List<GuideDay> Days { get; set; } = [];

    public Airing? OnNow { get; set; }

    public Airing? UpNext { get; set; }

    public DateTimeOffset? NextWindowStart { get; set; }

    public WeekStats Stats { get; set; } = new();

    public List<ComingUpItem> ComingUp { get; set; } = [];

    public string? Warning { get; set; }
}

/// <summary>A short description of a library item.</summary>
public class ItemSummary
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public bool HasPrimaryImage { get; set; }

    public bool HasBackdrop { get; set; }

    public string? Status { get; set; }

    public int RuntimeMinutes { get; set; }

    public string? OfficialRating { get; set; }

    public float? CommunityRating { get; set; }
}

/// <summary>A lineup entry with live status.</summary>
public class LineupEntryStatus
{
    public LineupEntry Entry { get; set; } = new();

    public ItemSummary? Item { get; set; }

    public bool Missing { get; set; }

    public int Total { get; set; }

    public int Watched { get; set; }

    public int Recorded { get; set; }

    public int Remaining { get; set; }

    public bool CaughtUp { get; set; }

    public ItemSummary? Next { get; set; }

    public string? NextLabel { get; set; }

    public SeriesAiringInfo? Airing { get; set; }

    public List<ItemSummary> Movies { get; set; } = [];
}
