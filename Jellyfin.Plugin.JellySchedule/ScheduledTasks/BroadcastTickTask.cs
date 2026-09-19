using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySchedule.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.ScheduledTasks;

/// <summary>
/// Periodically "broadcasts": freezes the airings that have started so the guide's history stays accurate
/// even when nobody has the app open.
/// </summary>
public class BroadcastTickTask : IScheduledTask
{
    private readonly ScheduleEngine _engine;
    private readonly ILogger<BroadcastTickTask> _logger;

    public BroadcastTickTask(ScheduleEngine engine, ILogger<BroadcastTickTask> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Jelly Schedule: broadcast tick";

    /// <inheritdoc />
    public string Key => "JellyScheduleBroadcastTick";

    /// <inheritdoc />
    public string Description => "Records which programmes have aired so the TV guide history is accurate.";

    /// <inheritdoc />
    public string Category => "Jelly Schedule";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            var today = ScheduleEngine.TodayIn(ScheduleEngine.ResolveTimeZone(null));
            await _engine.BuildGuideAsync(today, 1, true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jelly Schedule: broadcast tick failed");
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(15).Ticks
        };
    }
}
