# Jellyfin Audio Downloader

A [Jellyfin](https://jellyfin.org) plugin that renders and downloads a compressed audio track of a movie, episode, season or series — with intros, outros, commercials and previews removed, and long stretches of dead air trimmed out.

This is useful for building playlists, listening to audiobooks, podcasts-style listening sessions, or grabbing "clean" audio for offline use without downloading the full video.

![Version](https://img.shields.io/badge/Jellyfin-10.11-blue)
![Framework](https://img.shields.io/badge/.NET-net9.0-blue)

## Features

- **One-click download** — a "Download Audio" button is added to movie, episode, season and series detail pages in the Jellyfin web client.
- **Season/series support** — downloading a season or series combines the audio of every episode into a single file, in episode order.
- **Segment removal** — cuts detected intro, outro, commercial, preview and recap segments using Jellyfin media segments (as produced by the [Intro Skipper](https://github.com/intro-skipper/intro-skipper) plugin).
- **Dead air removal** — long silent stretches are detected and trimmed, configurable by duration and threshold.
- **Audio track selection** — picks the track you want when a file has multiple languages, with an optional preferred-language fallback.
- **MP3 or M4A/AAC output** — with configurable bitrates and channel downmixing.
- **Streaming download** — rendering happens server-side with a two-concurrency limiter, and the finished file is streamed straight to the browser as an attachment.

## Requirements

- **Jellyfin Server** 10.11.x (the plugin targets the `net9.0` framework and ABI `10.11.0.0`).
- **ffmpeg** — Jellyfin's bundled ffmpeg is used automatically, so no separate install is needed.
- **(Optional) Intro Skipper** — needed to populate media segments for intro/outro/commercial/preview removal. Segment removal works only for items that have segments; dead air removal works with or without it.

## Installation

### From a repository manifest (recommended)

Add a plugin repository to Jellyfin pointing at your built manifest, for example:

1. In Jellyfin, go to **Dashboard → Plugins → Repositories** and click **Add repository**.
2. Enter a friendly name and the URL of the repository manifest, e.g. `https://your-host/manifest.json`.
3. Choose **Catalog → Audio Downloader → Install** and restart Jellyfin when prompted.

### Manual install

1. Build the plugin (see [Building from source](#building-from-source)) or grab the published `Jellyfin.Plugin.AudioDownloader.dll`.
2. Copy the `.dll` (and the accompanying `.xml` if present) into Jellyfin's `plugins` data directory:
   - Linux: `/var/lib/jellyfin/plugins`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins`
   - macOS: `/var/lib/jellyfin/plugins`
   - Docker: `/config/plugins`
3. Restart the Jellyfin server.
4. Check the plugin appears under **Dashboard → Plugins**.

> Make sure the plugin folder/`dll` name matches the assembly (`Jellyfin.Plugin.AudioDownloader`) so the plugin loads and its configuration can be saved.

### Verifying the web UI injection

On server startup the plugin injects a script tag into the web client's `index.html`:

```html
<script id="audio-downloader" src="../../AudioDownloader/script" defer></script>
```

You can confirm it landed by viewing the source of Jellyfin's home page after a restart. If the button does not appear after upgrading Jellyfin, restart the server once more so the injection runs against the current web bundle.

## Usage

1. Open a movie, episode, season or series in the Jellyfin web client.
2. Click the **Download Audio** button in the detail page menu / header area.
3. Choose the audio track (if more than one is available) and the output format.
4. The server renders the file and your browser downloads it as `{title}.mp3` or `{title}.m4a`.

### Behaviour notes

- Downloading a **movie or episode** yields that item's audio.
- Downloading a **season or series** yields all episodes' audio concatenated in episode order (`Season 01 S01E01, S01E02, …`).
- When a requested audio track index is missing, the plugin falls back to your configured preferred language and finally to the container default track.
- MP3 output is always limited to two channels; M4A/AAC honours the configured maximum channel count.

## Configuration

Settings live in **Dashboard → Plugins → Audio Downloader**.

| Setting | Default | Description |
| --- | --- | --- |
| Minimum silence length (s) | `3.0` | Silence longer than this is treated as dead air and removed. `0` disables dead air removal. |
| Silence threshold (dB) | `-45` | Audio level below this counts as silence. |
| Remove segments – Intros | ✅ | Cuts detected intro segments. |
| Remove segments – Outros | ✅ | Cuts detected outro segments. |
| Remove segments – Commercials | ✅ | Cuts detected commercial segments. |
| Remove segments – Previews | ✅ | Cuts detected preview segments. |
| Remove segments – Recaps | ❌ | Cuts detected recap segments. |
| Default format | `M4A (AAC)` | Output format used when the client does not request one. |
| MP3 bitrate (kbps) | `256` | MP3 encode bitrate. |
| AAC bitrate (kbps) | `192` | AAC encode bitrate. |
| Maximum output channels | `2` | Sources with more channels are downmixed. |
| Preferred audio language | *(empty)* | ISO language code (`eng`, `deu`, …) used for fallback track selection. |
| Administrators only | ❌ | When enabled, only users with the `Administrator` role may download audio. |

## HTTP API

All endpoints are relative to your server, e.g. `http://localhost:8096`. Authenticated calls need an `X-Emby-Token` header or `api_key` query parameter.

| Method | Endpoint | Description |
| --- | --- | --- |
| `GET` | `/AudioDownloader/script` | Serves the client-side JS bundle (public, used by the injected script tag). |
| `GET` | `/AudioDownloader/tracks?itemId={id}` | Lists the audio tracks available for an item (after season/series resolution). |
| `GET` | `/AudioDownloader/download?itemId={id}&stream={index}&format={mp3|m4a}` | Renders and downloads the audio file. `stream=-1` (default) uses the container default. |

### Example

```bash
# List tracks for an item
curl -H "X-Emby-Token: TOKEN" \
  "http://localhost:8096/AudioDownloader/tracks?itemId=<ITEM_ID>"

# Download an MP3
curl -OJ -H "X-Emby-Token: TOKEN" \
  "http://localhost:8096/AudioDownloader/download?itemId=<ITEM_ID>&stream=-1&format=mp3"
```

Rendering a season or series can take a while; the request streams the result once done, and the per-server concurrency limit (2 concurrent renders) keeps simultaneous jobs sane.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet).

```bash
git clone <this-repo-url>
cd jellyfin-plugin-audio-downloader
dotnet build Jellyfin.Plugin.AudioDownloader.sln -c Release
```

The plugin assembly is produced at:

```
Jellyfin.Plugin.AudioDownloader/bin/Release/net9.0/Jellyfin.Plugin.AudioDownloader.dll
```

### Running the tests

The repo ships an xUnit test suite (`Jellyfin.Plugin.AudioDownloader.Tests`) covering item resolution, audio-track enumeration, stream selection, channel downmixing and interval/filter-graph logic:

```bash
dotnet test Jellyfin.Plugin.AudioDownloader.sln
```

### Continuous integration

On every push to and pull request against the `main` branch, GitHub Actions runs the test suite, builds the solution, and (on push) packages the plugin zip + repository manifest via [jellyfin-plugin-repository-manager](https://github.com/oddstr13/jellyfin-plugin-repository-manager). Creating a GitHub Release for a version tag triggers the publish workflow that attaches the built zip to the release.

To build the distributable plugin `zip` and `manifest.json` for a repository, follow the shared plugin CI flow, or pack the assembly manually:

```bash
dotnet publish Jellyfin.Plugin.AudioDownloader/Jellyfin.Plugin.AudioDownloader.csproj \
  -c Release -o artifact
cd artifact
zip -r Jellyfin.Plugin.AudioDownloader_1.0.0.0.zip \
  Jellyfin.Plugin.AudioDownloader.dll
```

## How it works

1. The requested item is resolved — movies/episodes to themselves, seasons/series to their ordered episodes.
2. A per-item pass finds the selected audio stream (`-map 0:a:<index>`), detects silence boundaries with ffmpeg's `silencedetect`, and reads cut segments from Jellyfin media segments.
3. Track ranges that are *not* cut and not dead air are encoded per-item to a temp file (MP3 or AAC).
4. Segments are concatenated losslessly (`-c copy`), faststart is applied for M4A, and the result is streamed to the browser while the temp output is cleaned up automatically.

## Troubleshooting

- **Button doesn't appear / script not injected** — confirm `/AudioDownloader/script` is reachable and that `<script id="audio-downloader" …>` is present in the served `index.html`; restart the server if needed.
- **Plugin shows as NotSupported** — the installed Jellyfin release is not 10.11.x, or the plugin was built against a different ABI. Rebuild for your server version.
- **Segments not removed** — the item has no media segments. Install and run Intro Skipper on that item first; dead air removal works regardless.
- **Download takes a long time** — long seasons/series require per-episode analysis and encoding. Two renders run concurrently; others queue.
- **413 / request aborted** — very large outputs may hit reverse-proxy limits. Disable the admin-only restriction or raise proxy upload/download and timeout limits.

## License

The plugin is licensed under the [GPLv3](https://www.gnu.org/licenses/gpl-3.0.en.html). See [LICENSE](LICENSE) for the full text.

## Disclaimer

Please respect applicable copyright and licensing when downloading and redistributing content, and be aware that cut points are sample-accurate but prepared *automatically* — always spot-check the output.