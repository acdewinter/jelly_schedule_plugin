# Brief: Jelly Schedule TV — an Android TV companion app

This is a self-contained brief for building a dedicated Android TV app for the Jelly Schedule Jellyfin plugin (https://github.com/acdewinter/jelly_schedule_plugin). It is meant to be handed to a fresh coding session, in a **new repository**. Read `docs/API.md` alongside it; the app is a client of that API and of Jellyfin's own API.

## 1. Why this app exists

Jelly Schedule turns a Jellyfin library into a weekly TV channel: the household chooses viewing evenings, lines up shows and movies, and the plugin generates a TV guide. The plugin ships a web app, but the official Jellyfin Android TV app is native and cannot host plugin pages. The companion app brings the channel to the living-room TV with a native player.

The product feeling to aim for: **turning on the TV**. You open the app and, if something is on, it is already playing. Between programmes there is a countdown to the next one. When the evening's window is over, the channel goes off air and the app says so, on purpose. It is deliberately *not* a library browser; the official Jellyfin app remains for that, and the web app remains the place to edit the schedule.

## 2. Scope

**In scope (v1)**

1. Connect to a Jellyfin server (manual URL, plus local network discovery if cheap) and sign in with username/password or Quick Connect. Remember the server and token per device. Any Jellyfin user may sign in; show a banner when the signed-in user is not the household user (from `state`).
2. **Tune-in screen** (launch screen): calls `now`. If `OnNow` exists, start playback (respect `Settings.AutoplayOnOpen`; if off, show a big "Tune in" focus target). Otherwise show *Off air* with `UpNext`, a countdown, and buttons: Guide, Recordings, Watch early.
3. **Player**: full-screen Media3 ExoPlayer with a 10-foot overlay: channel bug (LIVE / RECORDING), title and subtitle, progress, "Next: … at 20:30". D-pad: centre = play/pause, left/right = seek ±10 s (long press ±60 s), down = controls, back = close. Subtitle and audio track menus. Auto-advance per the rules in `API.md` (next programme, countdown interstitial, off-air screen).
4. **Guide**: day selector (Mon–Sun, this week / next week) and a vertical list of the day's programmes with time, poster, title, badges (On now, Movie, Re-run, Premiere, Finale, Rec, Watched, Missed). Selecting a programme opens **Programme details**: poster/backdrop, overview, and actions: Watch (or Tune in / Resume), Record for later, Cancel recording, Mark watched / unwatched.
5. **Recordings**: list of deferred programmes with Play, Cancel; watched ones in a separate section.
6. **Settings**: server and account, sign out, "autoplay on launch" (client side), preferred subtitle language, "always transcode" fallback toggle, and a read-only summary of the household settings with the note that editing happens in the web app.

**Out of scope for v1** (do not build): editing viewing windows or the lineup, movie night settings, one-offs, blackouts, Sonarr/Radarr configuration, casting to other devices. Leave clear extension points.

**Nice to have after v1**: publish "On now" / "Up next" into the Google TV home screen (Watch Next row and a launcher channel via `androidx.tvprovider`); a phone layout so the same app runs on Android phones; picture-in-picture of the channel while browsing the guide.

## 3. Technical choices

- **Language / UI**: Kotlin 2.x, Jetpack Compose with **Compose for TV** (`androidx.tv:tv-foundation`, `androidx.tv:tv-material`). Focus handling is the whole game on a TV: every screen must be fully navigable with a D-pad, with an obvious initial focus and no focus traps. Test with the keyboard arrows in the emulator.
- **Playback**: **Media3** (`androidx.media3:media3-exoplayer`, `media3-exoplayer-hls`, `media3-ui`, `media3-datasource-okhttp`). Prefer direct play; fall back to HLS transcoding when the server says so. Build the Jellyfin device profile from the device's real decoder capabilities (`MediaCodecList`) so HEVC/AV1/AC-3 direct-play when the TV supports it; the official jellyfin-androidtv app has a good reference implementation (GPL-2.0: if code is copied, license this app GPL-2.0-or-later; otherwise MIT is fine).
- **Jellyfin API**: the official Kotlin SDK (`org.jellyfin.sdk:jellyfin-core`) for authentication, discovery, `PlaybackInfo`, playback reporting and images. Call the plugin endpoints under `/JellySchedule/` with OkHttp (or the SDK's HTTP client) using the same token and the header format in `API.md`. Use `kotlinx.serialization` with a lenient, case-insensitive configuration; plugin JSON is PascalCase, enums are strings, GUIDs are 32 hex characters, nulls are omitted.
- **Architecture**: single-activity, one `NavHost`, `ViewModel` + `StateFlow` per screen, a small repository layer (`ScheduleRepository`, `PlaybackRepository`, `SessionRepository`), manual DI or Koin. Keep it small; this is a three-screen app.
- **Android**: `minSdk 26`, `targetSdk 35`, `android.software.leanback` required, `android.hardware.touchscreen` not required, `LEANBACK_LAUNCHER` intent category, a 320×180 banner, `applicationId` such as `dev.jellyschedule.tv`. Landscape only. Keep the screen on while playing.
- **Images**: Coil with the Jellyfin image URLs (`/Items/{id}/Images/Primary?maxHeight=…`); they need no auth.
- **Time**: use `java.time`. Plugin timestamps carry the household offset; show the wall-clock part they contain and compare instants for "now". Keep a clock offset from `state.Now` against the device clock.

## 4. Screens and behaviour in detail

**Startup**: if no server/token, go to Connect. Else fetch `state` in parallel with `now`; on 401 clear the token and show sign-in; on network failure show a retry screen with the server address.

**Tune-in**: poll `now` every 30 s while visible. Live position rules and auto-advance rules are in `API.md` (section "Behaviour a client should reproduce"). Show the current programme's backdrop blurred behind the off-air card.

**Player**: report start/progress/stop with the SDK's play state API, every 10 s and on pause/seek. When `Me.Id != HouseholdUser.Id`, also `POST /JellySchedule/playstate` every 30 s and at the end (`Played: true` at ≥ 90 %). On stop of a transcode, call `DELETE /Videos/ActiveEncodings?deviceId=…&playSessionId=…`. External text subtitles: `/Videos/{itemId}/{mediaSourceId}/Subtitles/{index}/0/Stream.vtt?api_key=…` as a Media3 `SubtitleConfiguration`; embedded tracks when direct playing. Changing the audio track on a transcode means requesting `PlaybackInfo` again with `AudioStreamIndex` and restarting at the same position.

**Guide**: `guide?from=<monday>&days=7`; the day list is the mobile layout of the web app, which works well with a D-pad. Highlight today, dim past days, show `Blackout.Label` on away days, and "Off air" when a day has windows but nothing scheduled. Header stats from `Stats`.

**Programme details**: `Record` → `POST recordings`; `Cancel recording` → `DELETE recordings/{RecordingId}`; `Mark watched` → `POST playstate { Played: true }`. Refresh the guide afterwards. Hide record/watched actions for `ReRun`.

**Recordings**: `GET recordings`; Play uses the same player in non-live mode (resume from `PositionTicks`); Cancel → `DELETE recordings/{id}`.

## 5. Development and testing

- Repository layout: standard Gradle project (`app/`), version catalog (`gradle/libs.versions.toml`), Gradle wrapper committed, `.editorconfig`, MIT (or GPL, see above) license, README with sideloading instructions.
- Build: `./gradlew :app:assembleDebug`; run on an Android TV emulator image (API 34, 1080p) and on a real device via `adb connect <tv-ip>:5555 && adb install -r app-debug.apk`. If the coding session's sandbox cannot download the Android SDK, still write the complete project and rely on GitHub Actions to build; do not stop at a skeleton.
- **Mock server for development**: the plugin repo contains `tools/DevHost`, a .NET 10 program that runs the real plugin API against an in-memory fake library and mocks the Jellyfin endpoints a client needs (`Users/AuthenticateByName`, `Users/Me`, `Items`, images, `PlaybackInfo`, a sample video stream, subtitles, play-state reporting, `Sessions`). Run it with `JS_SAMPLE_VIDEO=/path/to/sample.mp4 dotnet run` in `tools/DevHost` and point the app at `http://10.0.2.2:5000` from the emulator; sign in as `household` with any password. Its `/dev/reset` and `/dev/watched/{id}` helpers seed state (see `tools/ui-test/ui-test.mjs` for an example that seeds windows, a lineup, a movie night, a watched episode and a recording).
- Tests: unit tests for JSON parsing of every plugin object (use real payloads captured from the DevHost), for the tune-in position rules and the auto-advance decision, and for the guide grouping. A small instrumented smoke test on the emulator is welcome but not required.
- CI: GitHub Actions workflow that builds a debug APK on every push and a signed release APK on `v*` tags (signing key from repository secrets), attaching it to a GitHub release.

## 6. Milestones

1. **Core**: connect + sign-in, tune-in screen, player with auto-advance and playback reporting. This alone is the product.
2. **Guide** with programme details and record / watched actions.
3. **Recordings**, settings, household-mismatch banner, error states, polish (animations, focus, remote long-press).
4. **Release**: CI, signed APK, README with sideloading; optional Watch Next integration.

## 7. Quality bar

- Everything works with a D-pad only; no touch assumptions; text sizes for a 10-foot UI (body ≥ 18 sp at 1080p).
- Robust against: server unreachable, token expiry, an empty schedule (`Warning: "empty"` → explain that setup happens in the web app), missing images, files that cannot be direct played (fall back to transcoding), the device clock being wrong (use the server's `Now`).
- Never hide the "off air" state behind a library UI; the point of the app is that the channel has hours.
