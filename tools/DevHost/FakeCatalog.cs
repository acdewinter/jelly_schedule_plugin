using System.Globalization;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellySchedule.Models;
using Jellyfin.Plugin.JellySchedule.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace JellySchedule.DevHost;

/// <summary>
/// An in-memory library: a few series (with episodes), movies and a collection, plus per-user play state.
/// </summary>
public sealed class FakeCatalog : ILibraryCatalog
{
    private readonly Dictionary<Guid, BaseItem> _items = new();
    private readonly Dictionary<Guid, List<Episode>> _episodes = new();
    private readonly Dictionary<Guid, List<Movie>> _collections = new();
    private readonly Dictionary<(Guid User, Guid Item), UserItemData> _userData = new();
    private readonly List<User> _users = new();

    public FakeCatalog()
    {
        var household = MakeUser("household", true, "11111111-1111-1111-1111-111111111111");
        MakeUser("kid", false, "22222222-2222-2222-2222-222222222222");
        HouseholdUser = household;

        AddSeries("The Bear", 2022, SeriesStatus.Continuing, 30, [8, 10, 10], "A young chef from the fine dining world returns to Chicago to run his family's sandwich shop.", "396857");
        AddSeries("Severance", 2022, SeriesStatus.Continuing, 50, [9, 10], "Mark leads a team of office workers whose memories have been surgically divided between their work and personal lives.", "371980");
        AddSeries("Slow Horses", 2022, SeriesStatus.Continuing, 45, [6, 6, 6, 6], "A team of British intelligence agents serve in a dumping ground department of MI5.", "397096");
        AddSeries("Seinfeld", 1989, SeriesStatus.Ended, 22, [5, 12, 23, 24, 22, 24, 24, 22, 24], "A show about nothing.", "79169");
        AddSeries("Frasier", 1993, SeriesStatus.Ended, 22, [24, 24, 24, 24, 24], "Dr. Frasier Crane moves back to Seattle.", "77811");

        var dune = AddMovie("Dune: Part Two", 2024, 166, "Paul Atreides unites with Chani and the Fremen.");
        var heat = AddMovie("Heat", 1995, 170, "A group of high-end professional thieves start to feel the heat.");
        var padd = AddMovie("Paddington 2", 2017, 103, "Paddington picks up a series of odd jobs to buy the perfect present.");
        var god = AddMovie("The Godfather", 1972, 175, "The aging patriarch of an organized crime dynasty transfers control to his son.");
        AddMovie("Knives Out", 2019, 130, "A detective investigates the death of a patriarch of an eccentric, combative family.");
        AddMovie("Arrival", 2016, 116, "A linguist works with the military to communicate with alien lifeforms.");
        AddCollection("Sunday Blockbusters", [dune, heat, god, padd]);
    }

    public User HouseholdUser { get; }

    public string? SampleVideoPath { get; set; }

    public IEnumerable<BaseItem> AllItems => _items.Values;

    public User? GetUser(Guid id) => _users.FirstOrDefault(u => u.Id == id);

    public IEnumerable<User> GetUsers() => _users;

    public BaseItem? GetItem(Guid id) => _items.TryGetValue(id, out var i) ? i : null;

    public UserItemData? GetUserData(User user, BaseItem item) => _userData.TryGetValue((user.Id, item.Id), out var d) ? d : null;

    public bool IsWatched(User user, BaseItem item) => GetUserData(user, item)?.Played == true;

    public void SavePlayState(User user, BaseItem item, long? positionTicks, bool? played)
    {
        var d = GetUserData(user, item) ?? new UserItemData { Key = item.Id.ToString("N") };
        if (played.HasValue)
        {
            d.Played = played.Value;
            d.PlaybackPositionTicks = 0;
            if (played.Value)
            {
                d.PlayCount++;
                d.LastPlayedDate = DateTime.UtcNow;
            }
        }
        else if (positionTicks.HasValue)
        {
            var runtime = item.RunTimeTicks ?? 0;
            if (runtime > 0 && positionTicks.Value >= runtime * 0.9)
            {
                d.Played = true;
                d.PlaybackPositionTicks = 0;
                d.PlayCount++;
            }
            else
            {
                d.PlaybackPositionTicks = positionTicks.Value;
            }

            d.LastPlayedDate = DateTime.UtcNow;
        }

        _userData[(user.Id, item.Id)] = d;
    }

    public IReadOnlyList<Episode> GetEpisodes(Series series, User user, bool includeSpecials)
        => _episodes.TryGetValue(series.Id, out var list) ? list.Where(e => includeSpecials || (e.ParentIndexNumber ?? 1) != 0).ToList() : [];

    public IReadOnlyList<Movie> ExpandMovies(LineupEntry entry, User user)
    {
        var item = GetItem(entry.ItemId);
        return item switch
        {
            Movie m => [m],
            BoxSet b => _collections.TryGetValue(b.Id, out var l) ? l : [],
            _ => []
        };
    }

    public HashSet<Guid> GetUnwatchedEpisodeIds(Series series, User user)
        => GetEpisodes(series, user, true).Where(e => !IsWatched(user, e)).Select(e => e.Id).ToHashSet();

    public HashSet<Guid> GetUnwatchedIds(IReadOnlyList<Guid> ids, User user)
        => ids.Where(id => GetItem(id) is { } item && !IsWatched(user, item)).ToHashSet();

    public IEnumerable<BaseItem> Search(string? term, IReadOnlyCollection<string> types)
    {
        return _items.Values
            .Where(i => types.Count == 0 || types.Contains(i.GetBaseItemKind().ToString()))
            .Where(i => string.IsNullOrWhiteSpace(term) || (i.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name);
    }

    public Series AddSeries(string name, int year, SeriesStatus status, int minutes, int[] episodesPerSeason, string overview, string? tvdb = null)
    {
        var series = new Series
        {
            Id = StableGuid("series:" + name),
            Name = name,
            ProductionYear = year,
            Status = status,
            Overview = overview,
            RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks,
            Path = "/media/tv/" + name,
            ImageInfos = FakeImages()
        };
        if (tvdb is not null)
        {
            series.ProviderIds["Tvdb"] = tvdb;
        }

        _items[series.Id] = series;
        var eps = new List<Episode>();
        for (var s = 1; s <= episodesPerSeason.Length; s++)
        {
            for (var e = 1; e <= episodesPerSeason[s - 1]; e++)
            {
                var ep = new Episode
                {
                    Id = StableGuid($"ep:{name}:{s}:{e}"),
                    Name = $"{name} {s}x{e:00}",
                    ParentIndexNumber = s,
                    IndexNumber = e,
                    SeriesId = series.Id,
                    SeriesName = name,
                    Overview = $"Episode {e} of season {s}.",
                    RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks,
                    PremiereDate = new DateTime(year + s - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(7 * e),
                    Path = $"/media/tv/{name}/Season {s:00}/{name} S{s:00}E{e:00}.mkv",
                    ImageInfos = FakeImages()
                };
                _items[ep.Id] = ep;
                eps.Add(ep);
            }
        }

        _episodes[series.Id] = eps;
        return series;
    }

    public Movie AddMovie(string name, int year, int minutes, string overview)
    {
        var movie = new Movie
        {
            Id = StableGuid("movie:" + name),
            Name = name,
            ProductionYear = year,
            Overview = overview,
            RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks,
            Path = $"/media/movies/{name} ({year})/{name}.mkv",
            ImageInfos = FakeImages()
        };
        _items[movie.Id] = movie;
        return movie;
    }

    public BoxSet AddCollection(string name, List<Movie> movies)
    {
        var set = new BoxSet { Id = StableGuid("boxset:" + name), Name = name, Overview = $"{movies.Count} movies", Path = "/collections/" + name, ImageInfos = FakeImages() };
        _items[set.Id] = set;
        _collections[set.Id] = movies;
        return set;
    }

    public void MarkWatched(Guid itemId, bool watched = true)
    {
        if (GetItem(itemId) is { } item)
        {
            SavePlayState(HouseholdUser, item, null, watched);
        }
    }

    public Episode? Episode(string seriesName, int season, int episode)
        => GetItem(StableGuid($"ep:{seriesName}:{season}:{episode}")) as Episode;

    public Series? Series(string name) => GetItem(StableGuid("series:" + name)) as Series;

    public Movie? Movie(string name) => GetItem(StableGuid("movie:" + name)) as Movie;

    public BoxSet? Collection(string name) => GetItem(StableGuid("boxset:" + name)) as BoxSet;

    public static Guid StableGuid(string key)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(bytes);
    }

    private static ItemImageInfo[] FakeImages() =>
    [
        new ItemImageInfo { Path = "/fake/primary.png", Type = ImageType.Primary, Width = 400, Height = 600 },
        new ItemImageInfo { Path = "/fake/backdrop.png", Type = ImageType.Backdrop, Width = 1280, Height = 720 }
    ];

    private User MakeUser(string name, bool admin, string id)
    {
        var user = new User(name, "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider")
        {
            Id = Guid.Parse(id)
        };
        user.SetPermission(PermissionKind.IsAdministrator, admin);
        _users.Add(user);
        return user;
    }
}
