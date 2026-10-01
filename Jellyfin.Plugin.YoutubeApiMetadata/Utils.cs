using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using YTVideo = Google.Apis.YouTube.v3.Data.Video;

namespace Jellyfin.Plugin.YoutubeApiMetadata
{
    public static class Utils
    {
        /// <summary>
        /// yt-dlp's recommended layout puts the upload date first: "20190113 - Title [id].mkv" or
        /// "2019-01-13 - Title.mkv". Some templates also prefix the channel: "Channel - 20190113 - Title".
        /// </summary>
        private static readonly Regex DatePrefixRegex = new(
            @"^(?:(?<channel>[^\[\]]+?)\s+-\s+)?(?<date>(?<y>\d{4})-?(?<m>\d{2})-?(?<d>\d{2}))\s*-\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BracketedIdRegex = new(
            @"\s*\[[a-zA-Z0-9\-_]{11}\]|\s*\[[a-zA-Z0-9\-_]{24}\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NonAlphanumericRegex = new(
            @"[^\p{L}\p{Nd}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex HandleRegex = new(
            Constants.YTHANDLE_RE,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Channel IDs are "UC" + 22 URL-safe base64 chars. The "UC" anchor matters for free text:
        // without it any 24-letter channel name with no spaces would be mistaken for an ID.
        private static readonly Regex ChannelIdRegex = new(
            @"^UC[a-zA-Z0-9\-_]{22}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex VideoIdRegex = new(
            @"^[a-zA-Z0-9\-_]{11}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex TrailingParentheticalRegex = new(
            @"\s*\([^)]*\)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Extracts a YouTube video ID (11 chars) or channel ID (24 chars) from a file name,
        /// expected between square brackets, e.g. "Some Video [dQw4w9WgXcQ].mkv".
        /// </summary>
        public static string GetYTID(string name)
        {
            var videoId = GetVideoId(name);
            return videoId.Length > 0 ? videoId : GetChannelId(name);
        }

        /// <summary>
        /// Extracts only a bracketed 11-char video ID from text, or "" if there is none. Use this
        /// (on the file name alone) for episodes, so a "[channelId]" in the parent folder is never
        /// mistaken for the video's ID.
        /// </summary>
        public static string GetVideoId(string name)
        {
            var match = Regex.Match(name, Constants.YTID_RE);
            return match.Success ? match.Value : string.Empty;
        }

        /// <summary>
        /// Extracts only a bracketed 24-char channel ID from text, or "" if there is none.
        /// </summary>
        public static string GetChannelId(string name)
        {
            var match = Regex.Match(name, Constants.YTCHANNEL_RE);
            return match.Success ? match.Value : string.Empty;
        }

        /// <summary>
        /// The last segment of a path, treating both "/" and "\" as separators regardless of the
        /// host OS (library paths can come from a differently-hosted Jellyfin or a network share).
        /// </summary>
        public static string GetLastPathSegment(string path)
        {
            var trimmed = path.TrimEnd('/', '\\');
            var index = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            return index < 0 ? trimmed : trimmed[(index + 1)..];
        }

        /// <summary>
        /// The path minus its last segment (the parent directory), separator-agnostic like
        /// <see cref="GetLastPathSegment"/>. Null when there is no parent.
        /// </summary>
        public static string? GetParentPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var trimmed = path.TrimEnd('/', '\\');
            var index = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            return index <= 0 ? null : trimmed[..index];
        }

        /// <summary>
        /// True when the text is a YouTube handle ("@rickastley"): the API can resolve those with an
        /// exact 1-unit lookup instead of a 100-unit search.
        /// </summary>
        public static bool IsHandle(string? text)
        {
            return !string.IsNullOrWhiteSpace(text) && HandleRegex.IsMatch(text.Trim());
        }

        /// <summary>
        /// Canonical form of a channel/video name for comparisons and cache keys: lower-cased,
        /// with every run of punctuation/whitespace collapsed to a single space. Also strips a
        /// leading "@" so a handle and the same text without it normalize identically.
        /// </summary>
        public static string NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var trimmed = name.Trim();
            if (trimmed.StartsWith('@'))
            {
                trimmed = trimmed[1..];
            }

            return NonAlphanumericRegex.Replace(trimmed, " ").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Picks the highest-resolution thumbnail URL available, or null if none is.
        /// </summary>
        public static string? GetBestThumbnailUrl(ThumbnailDetails? thumbnails)
        {
            if (thumbnails == null)
            {
                return null;
            }

            return thumbnails.Maxres?.Url
                ?? thumbnails.Standard?.Url
                ?? thumbnails.High?.Url
                ?? thumbnails.Medium?.Url
                ?? thumbnails.Default__?.Url;
        }

        /// <summary>
        /// The channel's banner artwork (brandingSettings.image.bannerExternalUrl) rendered at a
        /// given size, or null if the channel has no banner. The bare URL returns the raw upload;
        /// the suffix asks YouTube's image server for a specific width and crop.
        /// </summary>
        public static string? GetBannerUrl(Channel? channel, string sizeSuffix)
        {
            var url = channel?.BrandingSettings?.Image?.BannerExternalUrl;
            return string.IsNullOrEmpty(url) ? null : url + sizeSuffix;
        }

        /// <summary>
        /// Interprets free text a user may put where a channel is expected (the "YouTube" external
        /// ID field, the Identify name box, a pasted link) as one of: a channel ID, a handle, or a
        /// name to search for. Accepts bare values and every common channel URL form:
        /// youtube.com/channel/UC…, youtube.com/@handle, youtube.com/c/Name, youtube.com/user/Name.
        /// </summary>
        public static ChannelReference ParseChannelReference(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return ChannelReference.None;
            }

            var value = text.Trim();

            var urlPath = GetYouTubeUrlPath(value);
            if (urlPath != null)
            {
                var segments = urlPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    return ChannelReference.None;
                }

                var first = Uri.UnescapeDataString(segments[0]);
                if (first.StartsWith('@'))
                {
                    return new ChannelReference(ChannelReferenceKind.Handle, first);
                }

                if (segments.Length >= 2)
                {
                    var second = Uri.UnescapeDataString(segments[1]);
                    if (first.Equals("channel", StringComparison.OrdinalIgnoreCase))
                    {
                        return ChannelIdRegex.IsMatch(second)
                            ? new ChannelReference(ChannelReferenceKind.Id, second)
                            : ChannelReference.None;
                    }

                    if (first.Equals("c", StringComparison.OrdinalIgnoreCase) || first.Equals("user", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ChannelReference(ChannelReferenceKind.Name, second);
                    }
                }

                return ChannelReference.None;
            }

            if (ChannelIdRegex.IsMatch(value))
            {
                return new ChannelReference(ChannelReferenceKind.Id, value);
            }

            return IsHandle(value)
                ? new ChannelReference(ChannelReferenceKind.Handle, value)
                : new ChannelReference(ChannelReferenceKind.Name, value);
        }

        /// <summary>
        /// Extracts a video ID from free text a user may put where a video is expected: a bare
        /// 11-char ID or any common video URL (watch?v=, youtu.be/, /shorts/, /embed/, /live/).
        /// Returns null when the text is neither.
        /// </summary>
        public static string? ParseVideoReference(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var value = text.Trim();
            if (VideoIdRegex.IsMatch(value))
            {
                return value;
            }

            if (!Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var host = uri.Host.ToLowerInvariant();
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string? candidate = null;

            if (host == "youtu.be" || host.EndsWith(".youtu.be", StringComparison.Ordinal))
            {
                candidate = segments.FirstOrDefault();
            }
            else if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)
                || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal))
            {
                if (segments.Length >= 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = uri.Query.TrimStart('?')
                        .Split('&', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Split('=', 2))
                        .Where(kv => kv.Length == 2 && kv[0] == "v")
                        .Select(kv => Uri.UnescapeDataString(kv[1]))
                        .FirstOrDefault();
                }
                else if (segments.Length >= 2
                    && (segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)
                        || segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase)
                        || segments[0].Equals("live", StringComparison.OrdinalIgnoreCase)
                        || segments[0].Equals("v", StringComparison.OrdinalIgnoreCase)))
                {
                    candidate = segments[1];
                }
            }

            return candidate != null && VideoIdRegex.IsMatch(candidate) ? candidate : null;
        }

        /// <summary>
        /// The raw value stored under this plugin's provider key (what the user typed in the
        /// "YouTube" external ID field, or what a previous match saved), or null.
        /// </summary>
        public static string? GetStoredProviderValue(IReadOnlyDictionary<string, string>? providerIds)
        {
            return providerIds != null
                && providerIds.TryGetValue(Constants.PluginName, out var value)
                && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        /// <summary>
        /// Resolves a channel ID from (in order): an already-stored provider ID, the folder name
        /// convention "[channelId]", or (last resort) a bracketed ID in the series display name.
        /// A stored value that is not actually a channel ID (a handle or name typed into the ID
        /// field) is ignored here; see <see cref="ParseChannelReference"/> for those.
        /// </summary>
        public static string? ResolveChannelId(IReadOnlyDictionary<string, string>? providerIds, string? path, string? name)
        {
            var stored = ParseChannelReference(GetStoredProviderValue(providerIds));
            if (stored.Kind == ChannelReferenceKind.Id)
            {
                return stored.Value;
            }

            var fromPath = GetChannelId(GetLastPathSegment(path ?? string.Empty));
            if (!string.IsNullOrEmpty(fromPath))
            {
                return fromPath;
            }

            var fromName = string.IsNullOrEmpty(name) ? string.Empty : GetChannelId(name);
            return fromName.Length > 0 ? fromName : null;
        }

        /// <summary>
        /// The channel name a series folder was given, with any "[channelId]" suffix removed:
        /// "/media/Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]" → "Rick Astley". For an episode path,
        /// pass its parent directory.
        /// </summary>
        public static string? GetChannelNameFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var folder = GetLastPathSegment(path);
            var name = BracketedIdRegex.Replace(folder, string.Empty).Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Splits a video file name into the pieces needed to find it on YouTube without an ID:
        /// the title (extension, bracketed IDs and yt-dlp's "YYYYMMDD - " prefix removed) and the
        /// upload date when the name carries one.
        /// </summary>
        public static (string Title, DateTime? UploadDate) ParseEpisodeFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return (string.Empty, null);
            }

            var name = Path.GetFileNameWithoutExtension(GetLastPathSegment(path));
            name = BracketedIdRegex.Replace(name, string.Empty);

            DateTime? uploadDate = null;
            var match = DatePrefixRegex.Match(name);
            if (match.Success)
            {
                if (DateTime.TryParseExact(
                        match.Groups["y"].Value + match.Groups["m"].Value + match.Groups["d"].Value,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    uploadDate = parsed;
                    name = name[match.Length..];
                }
            }

            return (name.Trim(), uploadDate);
        }

        /// <summary>
        /// From a list of channel search hits, the one whose title (or handle) exactly matches the
        /// requested name once normalized; otherwise YouTube's top hit; null if there are none.
        /// </summary>
        public static SearchResult? PickBestChannelMatch(string name, IEnumerable<SearchResult> candidates)
        {
            var list = candidates.Where(c => !string.IsNullOrEmpty(c.Id?.ChannelId)).ToList();
            var wanted = NormalizeName(name);
            return list.FirstOrDefault(c => NormalizeName(c.Snippet?.Title) == wanted) ?? list.FirstOrDefault();
        }

        /// <summary>
        /// Stable re-ordering of full channel resources so exact title/handle matches for the query
        /// come first; everything else keeps its incoming (relevance) order.
        /// </summary>
        public static IEnumerable<Channel> OrderChannelsByMatch(string query, IEnumerable<Channel> channels)
        {
            var wanted = NormalizeName(query);
            return channels.OrderBy(c => IsExactChannelMatch(wanted, c) ? 0 : 1);
        }

        /// <summary>
        /// Stable re-ordering of full video resources so exact title matches for the query come
        /// first; everything else keeps its incoming (relevance) order.
        /// </summary>
        public static IEnumerable<YTVideo> OrderVideosByMatch(string query, IEnumerable<YTVideo> videos)
        {
            var wanted = NormalizeName(query);
            return videos.OrderBy(v => NormalizeName(v.Snippet?.Title) == wanted ? 0 : 1);
        }

        /// <summary>
        /// The video a local file most likely is, from a list of search hits for its title. Only
        /// two kinds of evidence are accepted, so a near-miss never gets stamped onto the wrong
        /// file: an exact normalized title match, or (when the file name carries an upload date)
        /// the top hit published on that same UTC day. Null when neither holds.
        /// </summary>
        public static YTVideo? PickBestVideoMatch(string title, DateTime? uploadDate, IEnumerable<YTVideo> candidates)
        {
            var list = candidates.Where(v => !string.IsNullOrEmpty(v.Id)).ToList();
            if (list.Count == 0)
            {
                return null;
            }

            var wanted = NormalizeName(title);
            var exact = list.FirstOrDefault(v => NormalizeName(v.Snippet?.Title) == wanted);
            if (exact != null)
            {
                return exact;
            }

            if (uploadDate.HasValue)
            {
                var top = list[0];
                var published = top.Snippet?.PublishedAtDateTimeOffset;
                if (published.HasValue && published.Value.UtcDateTime.Date == uploadDate.Value.Date)
                {
                    return top;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses brandingSettings.channel.keywords, which the API returns as a single string of
        /// space-separated terms where multi-word terms are double-quoted:
        /// <c>"rick astley" music 80s</c> → ["rick astley", "music", "80s"].
        /// </summary>
        public static IReadOnlyList<string> ParseChannelKeywords(string? keywords)
        {
            if (string.IsNullOrWhiteSpace(keywords))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            foreach (var ch in keywords)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    Flush();
                    continue;
                }

                current.Append(ch);
            }

            Flush();
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            void Flush()
            {
                var term = current.ToString().Trim();
                if (term.Length > 0)
                {
                    result.Add(term);
                }

                current.Clear();
            }
        }

        /// <summary>
        /// Turns a topicDetails.topicCategories entry (a Wikipedia URL such as
        /// "https://en.wikipedia.org/wiki/Lifestyle_(sociology)") into a human-readable genre
        /// ("Lifestyle"). Returns null for anything that isn't a Wikipedia article URL.
        /// </summary>
        public static string? TopicCategoryToGenre(string? topicUrl)
        {
            if (string.IsNullOrWhiteSpace(topicUrl) || !Uri.TryCreate(topicUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var segment = uri.Segments.LastOrDefault();
            if (string.IsNullOrEmpty(segment) || segment == "/")
            {
                return null;
            }

            var title = Uri.UnescapeDataString(segment.TrimEnd('/')).Replace('_', ' ').Trim();
            title = TrailingParentheticalRegex.Replace(title, string.Empty).Trim();
            return title.Length == 0 ? null : title;
        }

        /// <summary>
        /// Genres for a set of topic category URLs, de-duplicated and in API order.
        /// </summary>
        public static string[] TopicCategoriesToGenres(IEnumerable<string>? topicCategories)
        {
            if (topicCategories == null)
            {
                return Array.Empty<string>();
            }

            return topicCategories
                .Select(TopicCategoryToGenre)
                .Where(g => !string.IsNullOrEmpty(g))
                .Select(g => g!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Maps a YouTube Data API video to a Jellyfin Episode (a video = one episode of its channel's "show").
        /// Season/episode numbers are deliberately not set here: on Jellyfin 12, episodes that share the
        /// same (Series, Season, Episode) get silently collapsed into "alternate versions" of one item, so
        /// every video needs a distinct <see cref="Episode.IndexNumber"/> — which needs the sibling
        /// episodes already in the library, unavailable to this pure mapping. See
        /// <see cref="ComputeEpisodeIndex"/> and its caller in <c>YoutubeEpisodeProvider</c>.
        /// </summary>
        public static MetadataResult<Episode> VideoToEpisode(YTVideo video)
        {
            var snippet = video.Snippet;
            var item = new Episode
            {
                Name = snippet.Title,
                Overview = snippet.Description
            };
            item.ProviderIds.Add(Constants.PluginName, video.Id);

            if (snippet.PublishedAtDateTimeOffset.HasValue)
            {
                var published = snippet.PublishedAtDateTimeOffset.Value.UtcDateTime;
                item.PremiereDate = published;
                item.ProductionYear = published.Year;
                item.ForcedSortName = $"{published:yyyyMMdd}-{snippet.Title}";
            }

            if (snippet.Tags != null && snippet.Tags.Count > 0)
            {
                item.Tags = snippet.Tags.ToArray();
            }

            var genres = TopicCategoriesToGenres(video.TopicDetails?.TopicCategories);
            if (genres.Length > 0)
            {
                item.Genres = genres;
            }

            if (!string.IsNullOrEmpty(video.ContentDetails?.Duration))
            {
                try
                {
                    var duration = XmlConvert.ToTimeSpan(video.ContentDetails.Duration);

                    // Live/upcoming videos report a zero duration (e.g. "P0D") until they actually
                    // air; treat that as "unknown" rather than a genuine zero-length video.
                    if (duration > TimeSpan.Zero)
                    {
                        item.RunTimeTicks = duration.Ticks;
                    }
                }
                catch (FormatException)
                {
                    // Unparsable duration string; skip silently.
                }
            }

            var result = new MetadataResult<Episode> { HasMetadata = true, Item = item };

            if (!string.IsNullOrEmpty(snippet.ChannelTitle))
            {
                var person = new PersonInfo
                {
                    Name = snippet.ChannelTitle,
                    Type = PersonKind.Director
                };
                if (!string.IsNullOrEmpty(snippet.ChannelId))
                {
                    person.ProviderIds = new Dictionary<string, string> { { Constants.PluginName, snippet.ChannelId } };
                }

                result.People = new List<PersonInfo> { person };
            }

            return result;
        }

        /// <summary>
        /// Maps a YouTube Data API channel to a Jellyfin Series (a channel = one "show"), using
        /// every part the plugin requests: snippet (name, description, creation date, country,
        /// handle), brandingSettings (keywords → tags) and topicDetails (topic categories → genres).
        /// </summary>
        public static MetadataResult<Series> ChannelToSeries(Channel channel)
        {
            var snippet = channel.Snippet;
            var item = new Series
            {
                Name = snippet.Title,
                Overview = snippet.Description,
                HomePageUrl = GetChannelHomePageUrl(channel)
            };
            item.ProviderIds.Add(Constants.PluginName, channel.Id);

            if (snippet.PublishedAtDateTimeOffset.HasValue)
            {
                var published = snippet.PublishedAtDateTimeOffset.Value.UtcDateTime;
                item.PremiereDate = published;
                item.ProductionYear = published.Year;
            }

            var keywords = ParseChannelKeywords(channel.BrandingSettings?.Channel?.Keywords);
            if (keywords.Count > 0)
            {
                item.Tags = keywords.ToArray();
            }

            var genres = TopicCategoriesToGenres(channel.TopicDetails?.TopicCategories);
            if (genres.Length > 0)
            {
                item.Genres = genres;
            }

            var country = snippet.Country ?? channel.BrandingSettings?.Channel?.Country;
            if (!string.IsNullOrWhiteSpace(country))
            {
                item.ProductionLocations = new[] { CountryCodeToName(country) };
            }

            return new MetadataResult<Series> { HasMetadata = true, Item = item };
        }

        /// <summary>
        /// The channel's public URL: its handle URL ("https://www.youtube.com/@rickastley") when it
        /// has one, otherwise the ID-based URL.
        /// </summary>
        public static string? GetChannelHomePageUrl(Channel channel)
        {
            var customUrl = channel.Snippet?.CustomUrl;
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                return string.Format(CultureInfo.InvariantCulture, Constants.HandleUrl, customUrl.Trim().TrimStart('/'));
            }

            return string.IsNullOrEmpty(channel.Id) ? null : string.Format(CultureInfo.InvariantCulture, Constants.ChannelUrl, channel.Id);
        }

        /// <summary>
        /// "US" → "United States". Falls back to the code itself for anything .NET doesn't know.
        /// </summary>
        public static string CountryCodeToName(string code)
        {
            var trimmed = code.Trim().ToUpperInvariant();
            try
            {
                return new RegionInfo(trimmed).EnglishName;
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
        }

        /// <summary>
        /// Everything Jellyfin's "Identify" dialog can show for a channel, from the full channel
        /// resource: name, description, creation year/date, avatar.
        /// </summary>
        public static RemoteSearchResult ChannelToSearchResult(Channel channel)
        {
            var result = new RemoteSearchResult
            {
                Name = channel.Snippet?.Title,
                Overview = channel.Snippet?.Description,
                ImageUrl = GetBestThumbnailUrl(channel.Snippet?.Thumbnails),
                SearchProviderName = Constants.PluginName,
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, channel.Id } }
            };

            var published = channel.Snippet?.PublishedAtDateTimeOffset;
            if (published.HasValue)
            {
                result.PremiereDate = published.Value.UtcDateTime;
                result.ProductionYear = published.Value.UtcDateTime.Year;
            }

            return result;
        }

        /// <summary>
        /// Everything Jellyfin's "Identify" dialog can show for a video, from the full video
        /// resource: title, description, upload year/date, thumbnail.
        /// </summary>
        public static RemoteSearchResult VideoToSearchResult(YTVideo video)
        {
            var result = new RemoteSearchResult
            {
                Name = video.Snippet?.Title,
                Overview = video.Snippet?.Description,
                ImageUrl = GetBestThumbnailUrl(video.Snippet?.Thumbnails),
                SearchProviderName = Constants.PluginName,
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, video.Id } }
            };

            var published = video.Snippet?.PublishedAtDateTimeOffset;
            if (published.HasValue)
            {
                result.PremiereDate = published.Value.UtcDateTime;
                result.ProductionYear = published.Value.UtcDateTime.Year;
            }

            return result;
        }

        /// <summary>
        /// Computes a video's episode number as its chronological rank (1-based) among its
        /// already-known channel siblings, so every video in a channel gets a distinct number and
        /// none of them get collapsed together as "alternate versions" of the same episode on
        /// Jellyfin 12. Pure and side-effect-free: the caller fetches <paramref name="siblingEpisodes"/>
        /// from the library.
        /// </summary>
        public static int ComputeEpisodeIndex(
            string currentVideoId,
            DateTime premiereDate,
            IEnumerable<(string VideoId, DateTime PremiereDate)> siblingEpisodes)
        {
            var rank = siblingEpisodes.Count(s =>
                s.VideoId != currentVideoId
                && (s.PremiereDate < premiereDate
                    || (s.PremiereDate == premiereDate && string.CompareOrdinal(s.VideoId, currentVideoId) < 0)));

            return rank + 1;
        }

        /// <summary>
        /// The path of a youtube.com URL (scheme optional), or null if the text is not one.
        /// </summary>
        private static string? GetYouTubeUrlPath(string value)
        {
            if (value.Contains(' ') || !value.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var host = uri.Host.ToLowerInvariant();
            return host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : null;
        }

        private static bool IsExactChannelMatch(string normalizedQuery, Channel channel)
        {
            if (normalizedQuery.Length == 0)
            {
                return false;
            }

            return NormalizeName(channel.Snippet?.Title) == normalizedQuery
                || NormalizeName(channel.Snippet?.CustomUrl) == normalizedQuery;
        }
    }

    /// <summary>
    /// What a piece of user-supplied text identifying a channel turned out to be.
    /// </summary>
    public enum ChannelReferenceKind
    {
        /// <summary>Nothing usable.</summary>
        None,

        /// <summary>A channel ID ("UC…", 24 chars): fetch directly.</summary>
        Id,

        /// <summary>A handle ("@name"): exact 1-unit lookup.</summary>
        Handle,

        /// <summary>Anything else: a name to search for.</summary>
        Name
    }

    /// <summary>
    /// A parsed channel reference: its kind and the cleaned-up value (ID, "@handle", or name).
    /// </summary>
    public readonly record struct ChannelReference(ChannelReferenceKind Kind, string Value)
    {
        public static readonly ChannelReference None = new(ChannelReferenceKind.None, string.Empty);
    }
}
