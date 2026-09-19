using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySchedule.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.ScheduledTasks;

/// <summary>Refreshes upcoming-episode information from Sonarr / TVmaze.</summary>
public class RefreshAiringsTask : IScheduledTask
{
    private readonly ScheduleEngine _engine;
    private readonly AiringInfoService _airings;
    private readonly ILogger<RefreshAiringsTask> _logger;

    public RefreshAiringsTask(ScheduleEngine engine, AiringInfoService airings, ILogger<RefreshAiringsTask> logger)
    {
        _engine = engine;
        _airings = airings;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Jelly Schedule: refresh airing dates";

    /// <inheritdoc />
    public string Key => "JellyScheduleRefreshAirings";

    /// <inheritdoc />
    public string Description => "Looks up when the next episodes of your shows are broadcast (Sonarr or TVmaze).";

    /// <inheritdoc />
    public string Category => "Jelly Schedule";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _engine.GetLineupStatusAsync(true, true, cancellationToken).ConfigureAwait(false);
            await _airings.GetRadarrUpcomingAsync(true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jelly Schedule: airing refresh failed");
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(12).Ticks
        };
    }
}
