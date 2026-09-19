using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySchedule.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySchedule.Services;

/// <summary>
/// JSON-file persistence for the household schedule.
/// </summary>
public sealed class ScheduleStore : IDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<ScheduleStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ScheduleData? _data;

    public ScheduleStore(IApplicationPaths paths, ILogger<ScheduleStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(paths.PluginConfigurationsPath, "JellySchedule");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "schedule.json");
    }

    /// <summary>Gets a private copy of the current data.</summary>
    public ScheduleData Get()
    {
        _lock.Wait();
        try
        {
            return Clone(Load());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Mutates and persists the data atomically.</summary>
    public async Task<T> UpdateAsync<T>(Func<ScheduleData, T> mutate, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = Load();
            var result = mutate(data);
            data.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync(data, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Mutates and persists the data atomically.</summary>
    public Task UpdateAsync(Action<ScheduleData> mutate, CancellationToken cancellationToken = default)
        => UpdateAsync(
            d =>
            {
                mutate(d);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private static ScheduleData Clone(ScheduleData data)
        => JsonSerializer.Deserialize<ScheduleData>(JsonSerializer.SerializeToUtf8Bytes(data, _json), _json) ?? new ScheduleData();

    private static string NewKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private ScheduleData Load()
    {
        if (_data is not null)
        {
            return _data;
        }

        if (File.Exists(_path))
        {
            try
            {
                using var stream = File.OpenRead(_path);
                _data = JsonSerializer.Deserialize<ScheduleData>(stream, _json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Jelly Schedule: could not read {Path}; starting with an empty schedule", _path);
                try
                {
                    File.Copy(_path, _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture), true);
                }
                catch (Exception copyEx)
                {
                    _logger.LogWarning(copyEx, "Jelly Schedule: could not back up corrupt schedule file");
                }
            }
        }

        _data ??= new ScheduleData();
        if (string.IsNullOrEmpty(_data.Settings.CalendarKey))
        {
            _data.Settings.CalendarKey = NewKey();
        }

        return _data;
    }

    private async Task SaveAsync(ScheduleData data, CancellationToken cancellationToken)
    {
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, data, _json, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, _path, true);
        _data = data;
    }
}
