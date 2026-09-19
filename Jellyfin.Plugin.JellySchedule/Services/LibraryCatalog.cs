using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySchedule.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellySchedule.Services;


/// <summary>Library access used by the scheduler (abstracted for testing).</summary>
public interface ILibraryCatalog
{
    User? GetUser(Guid id);

    IEnumerable<User> GetUsers();

    BaseItem? GetItem(Guid id);

    UserItemData? GetUserData(User user, BaseItem item);

    bool IsWatched(User user, BaseItem item);

    void SavePlayState(User user, BaseItem item, long? positionTicks, bool? played);

    IReadOnlyList<Episode> GetEpisodes(Series series, User user, bool includeSpecials);

    IReadOnlyList<Movie> ExpandMovies(LineupEntry entry, User user);

    HashSet<Guid> GetUnwatchedEpisodeIds(Series series, User user);

    HashSet<Guid> GetUnwatchedIds(IReadOnlyList<Guid> ids, User user);
}

/// <summary>
/// Thin wrapper over the Jellyfin library used by the scheduler.
/// </summary>
public sealed class LibraryCatalog : ILibraryCatalog
{
    private readonly ILibraryManager _library;
    private readonly IUserDataManager _userData;
    private readonly IUserManager _users;

    public LibraryCatalog(ILibraryManager library, IUserDataManager userData, IUserManager users)
    {
        _library = library;
        _userData = userData;
        _users = users;
    }

    public User? GetUser(Guid id) => id == Guid.Empty ? null : _users.GetUserById(id);

    public IEnumerable<User> GetUsers() => _users.GetUsers();

    public BaseItem? GetItem(Guid id) => id == Guid.Empty ? null : _library.GetItemById(id);

    public UserItemData? GetUserData(User user, BaseItem item) => _userData.GetUserData(user, item);

    public bool IsWatched(User user, BaseItem item) => _userData.GetUserData(user, item)?.Played == true;

    public long GetPosition(User user, BaseItem item) => _userData.GetUserData(user, item)?.PlaybackPositionTicks ?? 0;

    public void SavePlayState(User user, BaseItem item, long? positionTicks, bool? played)
    {
        var data = _userData.GetUserData(user, item) ?? new UserItemData { Key = item.GetUserDataKeys()[0] };
        var reason = UserDataSaveReason.PlaybackProgress;
        if (played.HasValue)
        {
            data.Played = played.Value;
            if (played.Value)
            {
                data.PlaybackPositionTicks = 0;
                data.PlayCount = Math.Max(1, data.PlayCount);
                data.LastPlayedDate = DateTime.UtcNow;
                reason = UserDataSaveReason.PlaybackFinished;
            }
            else
            {
                data.PlaybackPositionTicks = 0;
                reason = UserDataSaveReason.TogglePlayed;
            }
        }
        else if (positionTicks.HasValue)
        {
            _userData.UpdatePlayState(item, data, positionTicks.Value);
            data.LastPlayedDate = DateTime.UtcNow;
        }

        _userData.SaveUserData(user, item, data, reason, System.Threading.CancellationToken.None);
    }

    /// <summary>Gets the playable episodes of a series in broadcast order.</summary>
    public IReadOnlyList<Episode> GetEpisodes(Series series, User user, bool includeSpecials)
    {
        var episodes = series.GetEpisodes(user, new DtoOptions(false), false)
            .OfType<Episode>()
            .Where(e => !e.IsVirtualItem && e.LocationType != LocationType.Virtual)
            .Where(e => includeSpecials || (e.ParentIndexNumber ?? 1) != 0)
            .ToList();

        return episodes
            .OrderBy(e => e.ParentIndexNumber ?? int.MaxValue)
            .ThenBy(e => e.IndexNumber ?? int.MaxValue)
            .ThenBy(e => e.PremiereDate ?? DateTime.MaxValue)
            .ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Expands a lineup entry into the movies it represents.</summary>
    public IReadOnlyList<Movie> ExpandMovies(LineupEntry entry, User user)
    {
        var item = GetItem(entry.ItemId);
        switch (item)
        {
            case Movie movie:
                return [movie];
            case BoxSet boxSet:
                return boxSet.GetChildren(user, true, new InternalItemsQuery(user)).OfType<Movie>().ToList();
            case Playlist playlist:
                return playlist.GetChildren(user, true, new InternalItemsQuery(user)).OfType<Movie>().ToList();
            case Folder folder:
                return folder.GetRecursiveChildren(user, new InternalItemsQuery(user) { IsVirtualItem = false }, out _).OfType<Movie>().ToList();
            default:
                return [];
        }
    }

    /// <inheritdoc />
    public HashSet<Guid> GetUnwatchedEpisodeIds(Series series, User user)
    {
        var query = new InternalItemsQuery(user)
        {
            SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
            IncludeItemTypes = [BaseItemKind.Episode],
            IsPlayed = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        };
        return _library.GetItemIds(query).ToHashSet();
    }

    /// <inheritdoc />
    public HashSet<Guid> GetUnwatchedIds(IReadOnlyList<Guid> ids, User user)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var query = new InternalItemsQuery(user)
        {
            ItemIds = ids.ToArray(),
            IsPlayed = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        };
        return _library.GetItemIds(query).ToHashSet();
    }

    public static int RuntimeMinutes(BaseItem item, int fallback)
    {
        if (item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0)
        {
            return Math.Max(1, (int)Math.Round(item.RunTimeTicks.Value / (double)TimeSpan.TicksPerMinute));
        }

        return fallback;
    }

    public static int TypicalEpisodeMinutes(IReadOnlyList<Episode> episodes)
    {
        var runtimes = episodes.Where(e => e.RunTimeTicks.HasValue && e.RunTimeTicks.Value > 0)
            .Select(e => (int)Math.Round(e.RunTimeTicks!.Value / (double)TimeSpan.TicksPerMinute))
            .OrderBy(m => m)
            .ToList();
        if (runtimes.Count == 0)
        {
            return 30;
        }

        return runtimes[runtimes.Count / 2];
    }

    public static string? ProviderId(BaseItem item, string name)
        => item.ProviderIds is not null && item.ProviderIds.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
