# Jelly Schedule HTTP API

Everything the web app does goes through this API, so any other client (a TV app, a phone app, a script) can do the same. All paths are relative to the Jellyfin server root, e.g. `https://jellyfin.example/JellySchedule/guide`.

## Authentication and conventions

- Every endpoint needs a normal Jellyfin user token unless marked *anonymous*. Send it the Jellyfin way:
  `Authorization: MediaBrowser Token="<token>", Client="My App", Device="Living room", DeviceId="<stable id>", Version="1.0"`
  Obtain the token with Jellyfin's own `POST /Users/AuthenticateByName` or Quick Connect. `integrations/test` additionally requires an administrator.
- JSON uses Jellyfin's conventions: **PascalCase** property names, enums as **strings**, GUIDs as **32 hex characters without dashes** (dashed GUIDs are accepted on input), and **null properties are omitted** from responses. Request bodies are matched case-insensitively.
- Timestamps are ISO 8601 with an explicit offset in the **household time zone** (`2026-09-22T20:00:00+02:00`). Display the wall-clock part as-is; compare instants for "now" logic. Dates are `yyyy-MM-dd`; times of day are `HH:mm`.
- Errors are `{ "Message": "..." }` with 400/404, `403` when the caller may not edit (see `CanEdit`), `401` when the token is missing or expired.

## Read endpoints

| Method | Path | Returns |
| --- | --- | --- |
| GET | `state` | `StateResponse` – settings, windows, lineup, movie night, who you are, who the household user is |
| GET | `guide?from=yyyy-MM-dd&days=7` | `GuideResult` – the schedule for a date range (max 62 days); `from` defaults to today |
| GET | `now` | `{ Now, OnNow?, UpNext?, NextWindowStart?, Today: Airing[] }` – cheap, poll this every 30–60 s |
| GET | `lineup?refresh=false` | `LineupEntryStatus[]` – lineup with progress and upcoming air dates |
| GET | `series/{id}/episodes` | `EpisodeInfo[]` for the "start from" picker |
| GET | `recordings` | `RecordingStatus[]` |
| GET | `radarr/upcoming?refresh=false` | `UpcomingMovie[]` (empty unless Radarr is configured) |
| GET | `calendar.ics?key=<CalendarKey>&days=21` | iCalendar feed (*anonymous*, key from `Settings.CalendarKey`) |
| GET | `app`, `app/{file}` | the bundled web app (*anonymous*) |

Calling `guide` or `now` has a side effect: any programme whose start time has passed is *frozen* so it stays put in the history. A server task does the same every 15 minutes.

## Write endpoints

| Method | Path | Body | Effect |
| --- | --- | --- | --- |
| POST | `recordings` | `{ ItemId, AiringStart?, LineupEntryId? }` | Record (defer) an episode or movie; the schedule moves on without it. Returns `Recording` |
| DELETE | `recordings/{id}` | – | Cancel a recording (the item returns to the schedule if unwatched) |
| DELETE | `recordings/watched` | – | Remove recordings that have been watched |
| POST | `playstate` | `{ ItemId, PositionTicks?, Played? }` | Update the **household user's** play state (position in 100 ns ticks, or mark played/unplayed). Use this when the signed-in user is not the household user; otherwise Jellyfin's normal playback reporting is enough |
| POST | `lineup` | `AddLineupRequest` | Add a series, movie, collection or playlist |
| PUT | `lineup/{id}` | `UpdateLineupRequest` | Change mode, days, episodes per airing, start-from, paused |
| DELETE | `lineup/{id}` | – | Remove from the lineup |
| POST | `lineup/reorder` | `{ Ids: [...] }` | New priority order |
| PUT | `windows` | `ViewingWindow[]` | Replace all viewing windows |
| PUT | `movie-night` | `MovieNightSettings` | |
| PUT | `settings` | `ScheduleSettings` | Household user and admin-only flag are only applied for administrators |
| PUT | `blackouts` | `Blackout[]` | Replace away dates |
| POST | `one-offs` | `{ Date, Start, ItemId, DurationMinutes? }` | Put a specific item on a specific evening |
| DELETE | `one-offs/{id}` | – | |
| POST | `regenerate` | – | Un-freeze the current and upcoming programmes so today is rebuilt |
| POST | `integrations/refresh` | – | Clear cached air dates and refetch |
| GET | `integrations/test?service=sonarr|radarr|tvmaze&url=&apiKey=` | – | Admin only; omit url/apiKey to test the saved configuration |

## Objects

```
StateResponse {
  Version: "1.0.0", Me: UserInfo, HouseholdUser?: UserInfo, Users: UserInfo[] (admins only), CanEdit: bool,
  Settings: ScheduleSettings, Windows: ViewingWindow[], Lineup: LineupEntry[], MovieNight: MovieNightSettings,
  Blackouts: Blackout[], OneOffs: OneOff[], Integrations: { Sonarr, Radarr, TvMaze: bool },
  ServerTimeZone: "Europe/Amsterdam", Now: timestamp
}
UserInfo { Id, Name, IsAdmin }
ScheduleSettings {
  HouseholdUserId, TimeZoneId ("" = server zone), SlotMinutes (30|60),
  FillMode: "Reruns"|"Nothing"|"MoreEpisodes", LiveMode: "Relaxed"|"Strict", AutoplayOnOpen: bool,
  IncludeSpecials: bool, RerunPicker: "Random"|"RoundRobin", AdminOnlyEditing: bool, CalendarKey, JoinGraceMinutes
}
ViewingWindow { Id, Days: ["Monday", ...], Start: "20:00", End: "22:00" (end <= start means past midnight), Label? }
LineupEntry {
  Id, ItemId, Kind: "Series"|"Movie"|"Collection"|"Playlist", Mode: "InOrder"|"ReRun", Name,
  Days: [] (empty = any viewing day), EpisodesPerAiring: 1..6, Order, Paused: bool, StartFrom?: "S03E01", AddedAt
}
MovieNightSettings { Days: [...], Position: "Start"|"End", Order: "AsAdded"|"Shuffle", StartTime?: "20:00" }
Blackout { Id, From: date, To: date, Label }
OneOff { Id, Date, Start, ItemId, Name, DurationMinutes? }

GuideResult {
  Now, TimeZone, From, To, SlotMinutes, EarliestStart?: "19:00", LatestEnd?: "22:30",
  Days: GuideDay[], OnNow?: Airing, UpNext?: Airing, NextWindowStart?: timestamp,
  Stats: { Programmes, Episodes, Movies, Reruns, ScheduledMinutes, Watched, Missed },
  ComingUp: ComingUpItem[], Warning?: "empty" | text
}
GuideDay { Date, DayName, IsToday, IsPast, Blackout?, Windows: [{ Start, End, Label? }], Airings: Airing[] }
Airing {
  Id (stable), Start, End, DurationMinutes (slot), RuntimeMinutes (actual),
  Kind: "Episode"|"Movie"|"ReRun"|"OneOff", ItemId, SeriesId?, LineupEntryId?,
  Title (episode or movie title), SeriesName?, Season?, Episode?, Overview?, Year?, OfficialRating?, CommunityRating?,
  HasPrimaryImage, SeriesHasPrimaryImage, HasBackdrop, IsSeasonPremiere, IsSeasonFinale, IsSeriesFinale, RunsOver,
  IsWatched, PositionTicks, IsRecorded, RecordingId?, IsMissed, IsOnNow, IsFrozen
}
ComingUpItem { SeriesId, LineupEntryId, SeriesName, Season, Episode, Title, AirDate?, Network?, Source: "sonarr"|"tvmaze", HasFile }

LineupEntryStatus {
  Entry: LineupEntry, Item?: ItemSummary, Missing: bool, Total, Watched, Recorded, Remaining, CaughtUp: bool,
  Next?: ItemSummary, NextLabel?: "S01E03 · Title", Airing?: SeriesAiringInfo, Movies: ItemSummary[] (collections/playlists; Status = "Unwatched"|"Recorded"|"Watched")
}
ItemSummary { Id, Name, Type: "Series"|"Movie"|"Episode"|"BoxSet"|"Playlist", Year?, Overview?, HasPrimaryImage, HasBackdrop, Status?, RuntimeMinutes, OfficialRating?, CommunityRating? }
SeriesAiringInfo { Source, Status?, Network?, NextEpisode?: UpcomingEpisode, Upcoming: UpcomingEpisode[], AiredNotAvailable: UpcomingEpisode[], FetchedAt, Error? }
UpcomingEpisode { Season, Episode, Title, AirDate?, HasFile }

RecordingStatus { Recording: { Id, ItemId, LineupEntryId?, AiringStart?, RecordedAt, Title, Subtitle? }, Item?: ItemSummary, SeriesName?, SeriesId?, Season?, Episode?, IsWatched, PositionTicks, Missing }
EpisodeInfo { Id, Season?, Episode?, Name, Watched, RuntimeMinutes }
UpcomingMovie { RadarrId, Title, Year?, TmdbId?, DigitalRelease?, PhysicalRelease?, InCinemas?, Status?, IsAvailable }

AddLineupRequest { ItemId, Mode?, Days?, EpisodesPerAiring?, StartFrom? }
UpdateLineupRequest { Mode?, Days?, EpisodesPerAiring?, StartFrom?, Paused?, ClearStartFrom?: bool }
```

## Behaviour a client should reproduce

- **Images**: use Jellyfin's image endpoints. For an episode airing use the series poster (`/Items/{SeriesId}/Images/Primary`) when `SeriesHasPrimaryImage`, otherwise `/Items/{ItemId}/Images/Primary`. Backdrops come from the series (episodes) or the item (movies).
- **Tune-in position**: in `Strict` live mode start at `now − Start` when that is less than `RuntimeMinutes`, else 0. In `Relaxed` mode resume from `PositionTicks` when it is non-zero, else start from 0.
- **Playback** goes through Jellyfin's standard flow: `POST /Items/{id}/PlaybackInfo` with a device profile, then direct play / direct stream / HLS transcode, and `POST /Sessions/Playing`, `/Sessions/Playing/Progress` (every ~10 s) and `/Sessions/Playing/Stopped`. Jellyfin marks the item watched at ~90 %. If `Me.Id != HouseholdUser.Id`, also mirror position and completion to `POST playstate` so the schedule advances.
- **After a programme ends**: call `now`. If `OnNow` is a different item, play it. Else if `UpNext` starts within ~3 hours, show a countdown to `UpNext.Start` (in `Relaxed` mode offer "start now"), then play it. Otherwise show "off air" with `NextWindowStart`.
- **Record** = "keep it for later; move the show on". **Missed** (`IsMissed`) = it aired, nobody watched it, and it will simply air again in that show's next slot.
- `Warning: "empty"` on the guide means nothing has been configured yet; send the user to the web app to set up viewing times and a lineup.
