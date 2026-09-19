// Development host: runs the real Jelly Schedule controller + engine against an in-memory fake library,
// and mocks the handful of Jellyfin endpoints the web app talks to. Not part of the plugin.
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellySchedule.Api;
using Jellyfin.Plugin.JellySchedule.Services;
using JellySchedule.DevHost;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var dataDir = Environment.GetEnvironmentVariable("JS_DATA") ?? Path.Combine(Path.GetTempPath(), "jellyschedule-devhost");
Directory.CreateDirectory(dataDir);
var sample = Environment.GetEnvironmentVariable("JS_SAMPLE_VIDEO");

builder.Services.AddSingleton<IApplicationPaths>(new FakePaths(dataDir));
builder.Services.AddSingleton<ScheduleStore>();
builder.Services.AddSingleton<FakeCatalog>(_ => new FakeCatalog { SampleVideoPath = sample });
builder.Services.AddSingleton<ILibraryCatalog>(sp => sp.GetRequiredService<FakeCatalog>());
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AiringInfoService>();
builder.Services.AddSingleton<ScheduleEngine>();
builder.Services.AddSingleton<IAuthorizationContext, FakeAuthorizationContext>();
builder.Services.AddAuthentication("Fake").AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("Fake", _ => { });
builder.Services.AddAuthorization(o =>
{
    o.DefaultPolicy = new AuthorizationPolicyBuilder("Fake").RequireAuthenticatedUser().Build();
    o.AddPolicy("RequiresElevation", p => p.AddAuthenticationSchemes("Fake").RequireAuthenticatedUser().RequireClaim("admin", "true"));
});
builder.Services.AddControllers()
    .AddApplicationPart(typeof(JellyScheduleController).Assembly)
    .AddJsonOptions(o =>
    {
        // Mirror Jellyfin's JSON conventions (PascalCase, enums as strings, GUIDs without dashes).
        var jf = Jellyfin.Extensions.Json.JsonDefaults.Options;
        o.JsonSerializerOptions.PropertyNamingPolicy = jf.PropertyNamingPolicy;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = jf.PropertyNameCaseInsensitive;
        o.JsonSerializerOptions.DefaultIgnoreCondition = jf.DefaultIgnoreCondition;
        o.JsonSerializerOptions.NumberHandling = jf.NumberHandling;
        foreach (var c in jf.Converters)
        {
            o.JsonSerializerOptions.Converters.Add(c);
        }
    });

var app = builder.Build();
var jsonOut = app.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
var catalog = app.Services.GetRequiredService<FakeCatalog>();
var store = app.Services.GetRequiredService<ScheduleStore>();
await store.UpdateAsync(d => { if (d.Settings.HouseholdUserId == Guid.Empty) { d.Settings.HouseholdUserId = catalog.HouseholdUser.Id; } });

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

IResult Json(object value) => Results.Text(JsonSerializer.Serialize(value, jsonOut), "application/json", Encoding.UTF8);
static string? TokenOf(HttpRequest r)
{
    var h = r.Headers.Authorization.ToString();
    var m = System.Text.RegularExpressions.Regex.Match(h, "Token=\"([^\"]+)\"");
    if (m.Success) return m.Groups[1].Value;
    return r.Query.TryGetValue("api_key", out var k) ? k.ToString() : null;
}
User? UserOf(HttpRequest r) => TokenOf(r) is { } t && FakeAuthHandler.Tokens.TryGetValue(t, out var uid) ? catalog.GetUser(uid) : null;
object UserDto(User u) => new { Id = u.Id, Name = u.Username, ServerId = "devserver", Policy = new { IsAdministrator = u.HasPermission(PermissionKind.IsAdministrator) } };
object ItemDto(BaseItem i, User u)
{
    var ud = catalog.GetUserData(u, i);
    return new
    {
        Id = i.Id, Name = i.Name, Type = i.GetBaseItemKind().ToString(), ProductionYear = i.ProductionYear, Overview = i.Overview, RunTimeTicks = i.RunTimeTicks,
        Status = (i as Series)?.Status?.ToString(), ImageTags = new { Primary = "fake" }, BackdropImageTags = new[] { "fake" },
        UserData = new { Played = ud?.Played ?? false, PlaybackPositionTicks = ud?.PlaybackPositionTicks ?? 0 }
    };
}

app.MapGet("/", () => Results.Redirect("/JellySchedule/app"));
app.MapGet("/web/index.html", () => Results.Text("<html><body style='font-family:sans-serif;padding:40px'>Fake Jellyfin web. <a href='/JellySchedule/app'>Open Jelly Schedule</a></body></html>", "text/html"));

app.MapPost("/Users/AuthenticateByName", async (HttpRequest r) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(r.Body);
    var name = body.TryGetProperty("Username", out var n) ? n.GetString() : null;
    var user = catalog.GetUsers().FirstOrDefault(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase));
    if (user is null) return Results.Unauthorized();
    var token = "tok-" + Guid.NewGuid().ToString("N");
    FakeAuthHandler.Tokens[token] = user.Id;
    return Json(new { AccessToken = token, ServerId = "devserver", User = UserDto(user) });
});
app.MapGet("/Users/Me", (HttpRequest r) => UserOf(r) is { } u ? Json(UserDto(u)) : Results.Unauthorized());
app.MapPost("/Sessions/Logout", () => Results.NoContent());
app.MapGet("/Sessions", (HttpRequest r) => UserOf(r) is null ? Results.Unauthorized() : Json(new[]
{
    new { Id = "tv-session", DeviceId = "living-room-tv", DeviceName = "Living Room TV", Client = "Jellyfin Android TV", UserName = "household", SupportsRemoteControl = true, Capabilities = new { PlayableMediaTypes = new[] { "Video", "Audio" } }, NowPlayingItem = (object?)null }
}));
app.MapPost("/Sessions/{id}/Playing", (string id, HttpRequest r) => { Console.WriteLine($"[fake] remote play on {id}: {r.QueryString}"); return Results.NoContent(); });

app.MapGet("/Items", (HttpRequest r) =>
{
    if (UserOf(r) is not { } u) return Results.Unauthorized();
    var types = (r.Query["IncludeItemTypes"].ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
    var limit = int.TryParse(r.Query["Limit"], out var l) ? l : 50;
    var items = catalog.Search(r.Query["searchTerm"], types).Where(i => i is not Episode).Take(limit).Select(i => ItemDto(i, u)).ToList();
    return Json(new { Items = items, TotalRecordCount = items.Count, StartIndex = 0 });
});
app.MapGet("/Items/{id:guid}", (Guid id, HttpRequest r) => UserOf(r) is { } u && catalog.GetItem(id) is { } i ? Json(ItemDto(i, u)) : Results.NotFound());
app.MapGet("/Items/{id:guid}/Images/{type}", (Guid id, string type) =>
{
    var item = catalog.GetItem(id);
    var name = item?.Name ?? "?";
    var hue = Math.Abs(name.GetHashCode(StringComparison.Ordinal)) % 360;
    var wide = string.Equals(type, "Backdrop", StringComparison.OrdinalIgnoreCase);
    var (w, hgt) = wide ? (1280, 720) : (400, 600);
    var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{hgt}' viewBox='0 0 {w} {hgt}'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='hsl({hue},60%,35%)'/><stop offset='1' stop-color='hsl({(hue + 60) % 360},70%,20%)'/></linearGradient></defs><rect width='100%' height='100%' fill='url(#g)'/><text x='50%' y='50%' fill='white' font-family='sans-serif' font-size='{(wide ? 64 : 36)}' font-weight='700' text-anchor='middle' dominant-baseline='middle'>{System.Net.WebUtility.HtmlEncode(name)}</text></svg>";
    return Results.Text(svg, "image/svg+xml");
});
app.MapPost("/Items/{id:guid}/PlaybackInfo", (Guid id, HttpRequest r) =>
{
    if (UserOf(r) is null) return Results.Unauthorized();
    var item = catalog.GetItem(id);
    if (item is null) return Results.NotFound();
    return Json(new
    {
        PlaySessionId = Guid.NewGuid().ToString("N"),
        MediaSources = new[]
        {
            new
            {
                Id = id.ToString("N"), Container = sample is not null ? Path.GetExtension(sample).TrimStart('.') : "mp4", Name = item.Name, Protocol = "File", Type = "Default", RunTimeTicks = item.RunTimeTicks,
                SupportsDirectPlay = true, SupportsDirectStream = true, SupportsTranscoding = true, TranscodingUrl = (string?)null, DefaultAudioStreamIndex = 1,
                MediaStreams = new object[]
                {
                    new { Type = "Video", Index = 0, Codec = "h264", DisplayTitle = "1080p H264" },
                    new { Type = "Audio", Index = 1, Codec = "aac", Language = "eng", DisplayTitle = "English - AAC - Stereo", IsDefault = true },
                    new { Type = "Audio", Index = 2, Codec = "aac", Language = "fre", DisplayTitle = "French - AAC - Stereo", IsDefault = false },
                    new { Type = "Subtitle", Index = 3, Codec = "subrip", Language = "eng", DisplayTitle = "English", IsTextSubtitleStream = true, SupportsExternalStream = true, IsExternal = false }
                }
            }
        }
    });
});
app.MapGet("/Videos/{id:guid}/stream.{container}", (Guid id, string container) =>
{
    if (sample is not null && File.Exists(sample))
    {
        var mime = Path.GetExtension(sample).ToLowerInvariant() switch { ".webm" => "video/webm", ".mkv" => "video/x-matroska", _ => "video/mp4" };
        return Results.File(sample, mime, enableRangeProcessing: true);
    }

    return Results.NotFound("No sample video configured (set JS_SAMPLE_VIDEO)");
});
app.MapGet("/Videos/{id:guid}/{ms}/Subtitles/{index:int}/0/Stream.vtt", (Guid id) => Results.Text("WEBVTT\n\n00:00:00.500 --> 00:00:04.000\nJelly Schedule test subtitle\n\n00:00:05.000 --> 00:00:09.000\n(fake caption track)\n", "text/vtt"));
app.MapDelete("/Videos/ActiveEncodings", () => Results.NoContent());

async Task<IResult> PlayState(HttpRequest r, bool stopped)
{
    if (UserOf(r) is not { } u) return Results.Unauthorized();
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(r.Body);
    if (!body.TryGetProperty("ItemId", out var idEl) || !Guid.TryParse(idEl.GetString(), out var id) || catalog.GetItem(id) is not { } item) return Results.NoContent();
    var pos = body.TryGetProperty("PositionTicks", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;
    catalog.SavePlayState(u, item, pos, null);
    Console.WriteLine($"[fake] playstate {(stopped ? "stopped" : "progress")} {item.Name} @ {TimeSpan.FromTicks(pos)} by {u.Username}");
    return Results.NoContent();
}
app.MapPost("/Sessions/Playing", (HttpRequest r) => PlayState(r, false));
app.MapPost("/Sessions/Playing/Progress", (HttpRequest r) => PlayState(r, false));
app.MapPost("/Sessions/Playing/Stopped", (HttpRequest r) => PlayState(r, true));
app.MapPost("/UserPlayedItems/{id:guid}", (Guid id, HttpRequest r) => { if (UserOf(r) is not { } u) return Results.Unauthorized(); catalog.SavePlayState(u, catalog.GetItem(id)!, null, true); return Results.NoContent(); });

// Test helpers (not part of Jellyfin): mark items watched, jump the clock.
app.MapPost("/dev/watched/{id:guid}", (Guid id, bool value) => { catalog.MarkWatched(id, value); return Results.NoContent(); });
app.MapGet("/dev/items", () => Json(catalog.AllItems.Where(i => i is not Episode).Select(i => new { i.Id, i.Name, Type = i.GetBaseItemKind().ToString() })));
app.MapGet("/dev/episode", (string series, int season, int episode) => catalog.Episode(series, season, episode) is { } e ? Json(new { e.Id, e.Name }) : Results.NotFound());
app.MapPost("/dev/reset", async () => { await store.UpdateAsync(d => { d.Windows.Clear(); d.Lineup.Clear(); d.Recordings.Clear(); d.Frozen.Clear(); d.OneOffs.Clear(); d.Blackouts.Clear(); d.MovieNight = new(); d.Settings.HouseholdUserId = catalog.HouseholdUser.Id; }); return Results.NoContent(); });

Console.WriteLine($"Jelly Schedule dev host. Data: {dataDir}. Sign in as 'household' (admin) or 'kid' with any password.");
app.Run();

/// <summary>Authenticates requests carrying a token issued by /Users/AuthenticateByName.</summary>
sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public static readonly Dictionary<string, Guid> Tokens = new() { ["devtoken"] = Guid.Parse("11111111-1111-1111-1111-111111111111") };

    private readonly FakeCatalog _catalog;

    public FakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, FakeCatalog catalog)
        : base(options, logger, encoder) => _catalog = catalog;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var m = System.Text.RegularExpressions.Regex.Match(Request.Headers.Authorization.ToString(), "Token=\"([^\"]+)\"");
        var token = m.Success ? m.Groups[1].Value : Request.Query["api_key"].ToString();
        if (string.IsNullOrEmpty(token) || !Tokens.TryGetValue(token, out var uid) || _catalog.GetUser(uid) is not { } user)
        {
            return Task.FromResult(AuthenticateResult.Fail("no token"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, user.Username), new Claim("uid", user.Id.ToString("N")), new Claim("token", token), new Claim("admin", user.HasPermission(PermissionKind.IsAdministrator) ? "true" : "false") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Fake"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Fake")));
    }
}

sealed class FakeAuthorizationContext : IAuthorizationContext
{
    private readonly FakeCatalog _catalog;

    public FakeAuthorizationContext(FakeCatalog catalog) => _catalog = catalog;

    public Task<AuthorizationInfo> GetAuthorizationInfo(HttpContext requestContext) => GetAuthorizationInfo(requestContext.Request);

    public Task<AuthorizationInfo> GetAuthorizationInfo(HttpRequest requestContext)
    {
        var m = System.Text.RegularExpressions.Regex.Match(requestContext.Headers.Authorization.ToString(), "Token=\"([^\"]+)\"");
        var token = m.Success ? m.Groups[1].Value : requestContext.Query["api_key"].ToString();
        var user = !string.IsNullOrEmpty(token) && FakeAuthHandler.Tokens.TryGetValue(token, out var uid) ? _catalog.GetUser(uid) : null;
        return Task.FromResult(new AuthorizationInfo { User = user, IsAuthenticated = user is not null, Token = token, Client = "Jelly Schedule", Device = "dev", DeviceId = "dev", Version = "1" });
    }
}

sealed class FakePaths : IApplicationPaths
{
    public FakePaths(string root)
    {
        ProgramDataPath = root;
        foreach (var p in new[] { "plugins", "plugins/configurations", "data", "cache", "log", "config", "temp", "images", "trickplay", "backup" })
        {
            Directory.CreateDirectory(Path.Combine(root, p));
        }
    }

    public string ProgramDataPath { get; }

    public string WebPath => Path.Combine(ProgramDataPath, "web");

    public string ProgramSystemPath => ProgramDataPath;

    public string DataPath => Path.Combine(ProgramDataPath, "data");

    public string ImageCachePath => Path.Combine(ProgramDataPath, "images");

    public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");

    public string PluginConfigurationsPath => Path.Combine(ProgramDataPath, "plugins", "configurations");

    public string LogDirectoryPath => Path.Combine(ProgramDataPath, "log");

    public string ConfigurationDirectoryPath => Path.Combine(ProgramDataPath, "config");

    public string SystemConfigurationFilePath => Path.Combine(ConfigurationDirectoryPath, "system.xml");

    public string CachePath => Path.Combine(ProgramDataPath, "cache");

    public string TempDirectory => Path.Combine(ProgramDataPath, "temp");

    public string VirtualDataPath => "%AppDataPath%";

    public string TrickplayPath => Path.Combine(ProgramDataPath, "trickplay");

    public string BackupPath => Path.Combine(ProgramDataPath, "backup");

    public void MakeSanityCheckOrThrow()
    {
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
    }
}
