# YouTube API Metadata (Jellyfin plugin)

![Build and Test Plugin](https://github.com/arb362/jellyfin-youtube-api-metadata/actions/workflows/build.yml/badge.svg)

> [!CAUTION]
> This plugin does **not** download YouTube videos. It only fetches metadata (title, description, thumbnails...) for videos you already have on disk. Use [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) (or similar) to download the videos themselves.

Provides metadata for local YouTube video libraries, fetched from the **official YouTube Data API v3**, no [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) dependency, no cookies.

This is a fork of [jimmy-ncc/jellyfin-youtube-api-metadata](https://github.com/jimmy-ncc/jellyfin-youtube-api-metadata) that adds **channel-name search** (folders and files don't need YouTube IDs in their names) and maps every field the API exposes.

## Compatibility

Built and tested against **Jellyfin 12.x** (`Jellyfin.Controller`/`Jellyfin.Data` 12.0.0, .NET 10). **Not compatible with Jellyfin 10.11.x or earlier** — those need an older release of the upstream plugin (see its [Releases page](https://github.com/jimmy-ncc/jellyfin-youtube-api-metadata/releases) for the last 10.11.x-compatible build).

## How it maps to Jellyfin

- A YouTube **channel** = a Jellyfin **Series**
- A **video** = an **Episode**

### What gets filled in

| Jellyfin field | Series (channel) | Episode (video) |
| --- | --- | --- |
| Name / Overview | channel title / description | video title / description |
| Premiere date, year | channel creation date | upload date |
| Tags | channel keywords (`brandingSettings`) | video tags |
| Genres | topic categories (`topicDetails`, e.g. "Music", "Gaming") | topic categories |
| Production location | channel country | — |
| Home page | `youtube.com/@handle` (or channel URL) | — |
| People | — | channel as *Director* |
| Runtime | — | video duration |
| Episode number | — | chronological rank within the channel |
| External link | YouTube channel | YouTube video |
| Images | avatar (Primary), banner (Backdrop + Banner) | thumbnail (Primary) |

## Finding channels and videos

The plugin locates a channel or video by, in order:

1. **A stored ID** — once anything has been matched (automatically or through *Identify*), the ID is saved on the item and used from then on.
2. **An ID in the name** — `Channel Name [UCxxxxxxxxxxxxxxxxxxxxxx]` for folders, `Title [dQw4w9WgXcQ]` for files. Free (1 quota unit per lookup) and unambiguous; use this when you can.
3. **The channel name or handle** — a folder named `Rick Astley` or `@RickAstleyYT` is looked up on YouTube. A handle is an exact lookup (1 quota unit). A plain name is a search (100 quota units): the plugin takes an exact title match if one is in the top hits, otherwise YouTube's top hit. Either way the result is cached on disk and stored on the item, so the search is paid **once per folder**.
4. **The video title** — a file with no `[videoId]` is searched for *within its channel* (100 quota units, once per file). To avoid stamping the wrong metadata on a file, a match is only accepted if the title is an exact match (ignoring case and punctuation), or the top hit was uploaded on the date in the file name (`20091025 - Title.mkv`). Files in a folder whose channel could not be determined are left alone.

In the **Identify** dialog you can type into either field. Both the "YouTube" ID field and the Name box accept a channel ID (`UC…`), a handle (`@RickAstleyYT`), a channel name, or a pasted channel URL; for episodes, a video ID or any video URL (`watch?v=`, `youtu.be/`, `/shorts/`). The same goes for the YouTube external ID in the metadata editor: save a handle or URL there and the next refresh replaces it with the real channel ID.

Steps 3 and 4 can be switched off in the plugin settings. The manual **Identify** dialog uses the same searches (channel name/handle for a series, title within the channel for an episode) and shows full details — description, year, avatar/thumbnail — for every candidate.

### Quota

The YouTube Data API allows 10 000 units per day by default. Every lookup by ID costs 1 unit (and up to 50 IDs share one call), every search costs 100. With IDs in your file names a full library scan costs a few units; with names only, the first scan costs roughly 100 units per channel folder plus 100 per ID-less video file, and next to nothing afterwards thanks to the cache.

## File naming convention

Download the videos with [`yt-dlp`](https://github.com/yt-dlp/yt-dlp), using an output template that matches the layout below:

```
yt-dlp -o "<library>/%(uploader)s/%(upload_date)s - %(title)s [%(id)s].%(ext)s" <url>
```

Which produces:

```
<library>/<Channel Name>/<upload_date> - <title> [<videoId>].<ext>
```

> [!IMPORTANT]
> Don't repeat the channel name as a filename prefix (e.g. `<Channel Name> - <upload_date> - ...`) — on Jellyfin 12.x that pattern makes every video in the channel collapse into one episode with a version picker instead of appearing separately.

Channel folders can be named `<Channel Name>`, `@<handle>`, or `<Channel Name> [<channelId>]`. Video files can be `<title>.<ext>`, `<upload_date> - <title>.<ext>`, or either with a ` [<videoId>]` suffix; the ID form is cheapest and never ambiguous.

## Setup

1. Get a YouTube Data API v3 key from the [Google Cloud Console](https://console.cloud.google.com/apis/credentials) (enable the "YouTube Data API v3" API on the project first).
2. Install the plugin (see below), then set the key in **Dashboard → Plugins → YouTube API Metadata**. The same page has the cache TTL (default: 30 days), the two name-lookup toggles and the number of *Identify* results.
3. Point a **Shows** library at your YouTube video folders.

## Installation

### Via a plugin repository (recommended)

Dashboard → Plugins → Repositories → Add:

```
https://raw.githubusercontent.com/arb362/jellyfin-youtube-api-metadata/main/manifest.json
```

Then install "YouTube API Metadata" from Dashboard → Plugins → Catalog and restart Jellyfin.

The manifest points at the plugin zip checked into `releases/` in this repository, so it works as soon as the repository is on GitHub — no GitHub Release required.

### Manual

Download `releases/youtube-api-metadata_<version>.zip`, extract it into `<jellyfin data dir>/plugins/YoutubeApiMetadata_<version>/`, and restart Jellyfin.

## Development

```bash
dotnet build
dotnet test
```

To cut a new version: bump `version` and `changelog` in `build.yaml` and `Version`/`AssemblyVersion`/`FileVersion` in the `.csproj`, then

```bash
scripts/build-release.sh            # runs the tests, builds releases/<zip>, updates manifest.json
```

and commit `releases/` and `manifest.json`. After forking, run `scripts/set-github-repo.sh <owner>/<repo>` once to point the manifest (and this README) at your repository.

The upstream GitHub Actions release workflow (`.github/workflows/publish.yml`) is still present and also works if you prefer publishing through GitHub Releases.

A devcontainer is provided (`.devcontainer/`) with a full Jellyfin dev environment (server + web client built from source).

## License

[GNU AGPL v3.0](LICENSE)
