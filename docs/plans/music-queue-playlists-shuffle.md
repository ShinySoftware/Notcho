# Plan: shuffle, up next, playlists and search in the music tab

The music tab (`src/ShinyNotch.Core/Views/MediaTab.cs`) shows what's playing, a scrubber and previous / play-pause / next. Users asked for three more things:

1. **Shuffle**: turn it on or off.
2. **Up next**: see the tracks coming after this one.
3. **Pick something to play**: choose a playlist, or search for a song.

Shiny Notch is a remote control, not a player. Each platform reaches the player its own way, so what can be built depends on the platform and the player. This plan builds what each one can actually do, and hides any control the current player can't back up.

## What each platform can do

| | Mac: Apple Music (AppleScript) | Mac: Spotify (AppleScript) | Windows (system media controls) | Linux (MPRIS) |
|---|---|---|---|---|
| Shuffle: read & set | ✅ `shuffle enabled` | ✅ `shuffling` | ✅ `PlaybackInfo.IsShuffleActive` / `TryChangeShuffleActiveAsync`, if the app reports it (`Controls.IsShuffleEnabled`) | ✅ `Shuffle` property, if the player has it (`CanControl`, property present) |
| Up next | ⚠️ Approximate: the tracks after the current one in `current playlist`. AppleScript can't see the real Up Next queue | ❌ Web API only | ❌ | ⚠️ Optional `org.mpris.MediaPlayer2.TrackList`, which few players have (Spotify doesn't) |
| List & play playlists | ✅ `name of user playlists`, `play playlist "X"` | ❌ Can only `play track "spotify:…"`. Listing needs the Web API | ❌ | ⚠️ Optional `org.mpris.MediaPlayer2.Playlists` (Rhythmbox has it; Spotify and browsers don't) |
| Search & play | ✅ `search playlist "Library" for "…"`, then `play` a result | ❌ Web API only | ❌ | ❌ |

Spotify's queue, playlists and search all need the Spotify Web API with an OAuth sign-in. That's Phase 4, a separate decision.

## Phase 0: check our assumptions

Do these by hand with `osascript` before writing app code, and note the results here.

1. **Music, up next:** with shuffle **off**, compare `name of tracks of current playlist` after `index of current track` against Music's Up Next panel. Repeat with shuffle **on** to confirm the list is wrong then. (We expect it to be, so the list will be hidden while shuffle is on.)
2. **Music, playing from search or the library:** check what `current playlist` returns when a song was started from search, an album page, or Apple Music streaming (not the library). It might be "Library" with thousands of tracks, or it might fail.
3. **Music, timing:** time `search playlist "Library" for "x"` and `name of user playlists` on a large library. Both run on the main thread (NSAppleScript), so if either takes more than about 200ms we need to cache results or limit how many we read.
4. **Music, playing a search result:** check that `play` on a track from search starts it without wiping the user's queue in a way that would surprise them, and see what Up Next contains afterwards.
5. **Spotify:** confirm that reading and setting `shuffling` works on the current version.
6. **Windows:** confirm `IsShuffleEnabled` and `TryChangeShuffleActiveAsync` with Spotify, Media Player and a browser.
7. **Linux:** confirm `Shuffle` with Spotify, Rhythmbox and Firefox, and check which of them have `TrackList` / `Playlists`.

**Done when:** each ✅ / ⚠️ above has been confirmed or corrected.

## Phase 1: shuffle on every platform

### Service

Add to `NowPlaying`:

```csharp
/// <param name="Shuffle">Shuffle is on, or null when the player can't report or change it.</param>
bool? Shuffle
```

A `null` hides the button, so players that don't support shuffle never show a dead control. Shuffle is part of the reading, so it updates with everything else and raises `Changed`. `TrackKey` stays the same, because changing shuffle isn't a new track.

Add to `INowPlayingService`:

```csharp
/// <summary>Turns shuffle on or off. Ignored when <see cref="NowPlaying.Shuffle"/> is null.</summary>
void SetShuffle(bool on);
```

- **Mac** (`MacNowPlayingService`): add `shuffle enabled` (Music) / `shuffling` (Spotify) to the `Query` script's list and read it as item 7. The distributed notifications don't carry shuffle, so `OnPlayerInfo` keeps the last known value for the same player and `Refresh` corrects it. `SetShuffle` → `Command("set shuffle enabled to true")` for Music and `Command("set shuffling to true")` for Spotify. The verb differs by player, so `Command` needs the player, or `SetShuffle` builds the verb itself.
- **Windows** (`WindowsNowPlayingService`): in `ReadAsync`, `playback.Controls.IsShuffleEnabled ? playback.IsShuffleActive ?? false : null`. `SetShuffle` → `Command(s => s.TryChangeShuffleActiveAsync(on).AsTask())`.
- **Linux** (`LinuxNowPlayingService`): read `Shuffle` with the other player properties. If it's missing or the read fails, use null. `SetShuffle` → set the `Shuffle` property over DBus, then `Refresh`.

### UI

In `MediaTab`, add a `shuffle` symbol button to the left of previous, only when `now.Shuffle is not null`. When shuffle is on, tint it (`tint`); when off, use `Ui.Secondary`. `Tick` updates its colour the way it already updates the play button's symbol. Hint: "Shuffle".

The transport row is centred, so one extra button on the left pushes play off-centre. Either add a matching spacer (or the Phase 3 "browse" button) on the right, or put shuffle outside the centred stack.

**Done when:** shuffle shows and works with Music and Spotify on Mac, Spotify / Media Player on Windows, and Spotify / Rhythmbox on Linux, and is hidden for a player without it.

## Phase 2: up next (Apple Music on Mac)

### Service

A new optional interface keeps everything that isn't on every platform out of `INowPlayingService`:

```csharp
public sealed record QueuedTrack(string Id, string Title, string Artist, double Duration);

/// <summary>Browsing and choosing what plays: only some platforms, and only some players, can do it.</summary>
public interface IMusicLibrary
{
    /// <summary>The current player can list what's coming up right now.</summary>
    bool CanShowUpNext { get; }

    /// <summary>The next few tracks after the current one, or empty.</summary>
    Task<IReadOnlyList<QueuedTrack>> GetUpNextAsync(int count, CancellationToken ct = default);

    /// <summary>Jumps to a track from the up next list.</summary>
    Task PlayAsync(QueuedTrack track, CancellationToken ct = default);
}
```

On Mac, `MacNowPlayingService` implements it as well, since it already owns the AppleScript runner, the timeouts and knows which player is current. Register it with `[Singleton]` alongside, or resolve it as `media as IMusicLibrary` in `NotchController` (no DI change needed). Windows has no implementation. Linux gets one in Phase 2b.

- `CanShowUpNext` = the current player is Music, `Shuffle == false`, and Phase 0 step 2 says `current playlist` is meaningful for this track.
- `GetUpNextAsync`: one script that reads `index of current track`, then `{persistent ID, name, artist, duration}` of the next `count` tracks of `current playlist`, wrapped in `with timeout`. Use persistent IDs, not indexes, because indexes change when the playlist does.
- `PlayAsync`: `play (first track of current playlist whose persistent ID is "…")`.
- Cache the list by `TrackKey`, so reopening the panel doesn't run the script again for the same track.

### UI

Below the now-playing card, an "Up next" section with the next 5 tracks, one row each (title and artist, duration on the right). Tapping a row plays it. Only shown when `CanShowUpNext`. It loads after the card has drawn, so opening the tab is never slowed by AppleScript.

Panel height is limited (up to 480pt), so cap the rows rather than scroll, or put the list in a short `ScrollView`.

### Phase 2b: Linux TrackList (optional)

If Phase 0 finds players that really have `TrackList`, implement `IMusicLibrary` in `LinuxNowPlayingService` with `Tracks` + `GetTracksMetadata` and `GoTo`. Skip it if none of the common ones do.

## Phase 3: playlists and search (Apple Music on Mac)

### Service

Add to `IMusicLibrary`:

```csharp
public sealed record PlaylistInfo(string Id, string Name, int TrackCount);

bool CanBrowse { get; }
Task<IReadOnlyList<PlaylistInfo>> GetPlaylistsAsync(CancellationToken ct = default);
Task PlayPlaylistAsync(PlaylistInfo playlist, bool shuffle, CancellationToken ct = default);
Task<IReadOnlyList<QueuedTrack>> SearchAsync(string query, int limit, CancellationToken ct = default);
```

- `CanBrowse` = Music is running. (Asking Music while it's closed would launch it, so don't. The tab says "Open Music to browse".)
- `GetPlaylistsAsync`: `{persistent ID, name, count of tracks}` of `user playlists`, cached for a few minutes. Smart and folder playlists: decide in Phase 0 whether to list them.
- `PlayPlaylistAsync`: set `shuffle enabled` first if asked, then `play (first user playlist whose persistent ID is "…")`. This covers "shuffle a playlist" in one tap.
- `SearchAsync`: `search playlist "Library" for "…" only songs`, the first `limit` results. Escape the query before putting it into the script (quotes and backslashes), because it's user input going into AppleScript.

### UI

A "browse" symbol button (for example `music.note.list`) in the transport row, on the right to balance shuffle. It's only shown when `CanBrowse`. It swaps the area under the card for a browser:

- a search field at the top. Search after a short pause in typing (about 300ms), and cancel the previous search when a new one starts;
- with the field empty: the playlists, each with a play button and a shuffle-play button;
- with text: matching songs, tap to play;
- a back / close button to return to the now-playing view.

Typing in the panel needs keyboard focus, so check how the Notes and Calculator tabs take and release focus, and do it the same way. Esc should close the browser, not the panel.

**Done when:** you can search your library and play a song, and pick a playlist and shuffle-play it, all from the notch, with Music. With Spotify, or on Windows/Linux, nothing new shows except shuffle.

## Phase 4 (separate decision): Spotify Web API

This would give Spotify the real queue (`GET /me/player/queue`), playlists, search and play, on every platform, not just Mac. It costs:

- registering a Spotify developer app, and a PKCE OAuth sign-in in the app (a loopback redirect: `Shiny.Net.HttpServer` has OAuth loopback support);
- storing the refresh token securely per platform;
- Spotify Premium is required for the playback endpoints (play, queue);
- rate limits, and Spotify's developer terms for a free app.

If we do it, it slots in as another `IMusicLibrary` used when the current player is Spotify, so the Phase 2–3 UI works unchanged.

## Files touched

- `src/ShinyNotch.Core/Platform/INowPlayingService.cs`: `Shuffle`, `SetShuffle`
- `src/ShinyNotch.Core/Platform/IMusicLibrary.cs`: new
- `src/ShinyNotch.Core/Views/MediaTab.cs`: shuffle button, up next, browser
- `src/ShinyNotch.Core/NotchController.cs`: pass the library to `MediaTab`
- `src/ShinyNotch/Platform/MacNowPlayingService.cs`: shuffle, `IMusicLibrary`
- `src/ShinyNotch.Windows/Platform/WindowsNowPlayingService.cs`: shuffle
- `src/ShinyNotch.Linux/Platform/LinuxNowPlayingService.cs`: shuffle (and TrackList in 2b)
- `README.md` / website: update the media feature description and screenshot

## Open questions

- Should up next show while shuffle is on, labelled "Playlist order", or stay hidden? (Plan: hidden.)
- Should the browser replace the now-playing card, or sit under a smaller one? Panel height decides it.
- Phase 4: worth it, given the Premium requirement?
