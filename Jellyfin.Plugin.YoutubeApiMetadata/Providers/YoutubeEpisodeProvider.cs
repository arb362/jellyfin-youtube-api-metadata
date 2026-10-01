using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Providers
{
    /// <summary>
    /// Fetches Episode metadata (a single YouTube video) from the YouTube Data API v3.
    /// A video is located by (in order) its stored provider ID, the "[videoId]" in its file name,
    /// or — when the file name carries no ID — a title search within its channel.
    /// </summary>
    public class YoutubeEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IYoutubeMetadataResolver _resolver;
        private readonly ILibraryManager _libraryManager;
        private readonly Func<PluginConfiguration> _getConfiguration;

        public YoutubeEpisodeProvider(IYoutubeMetadataResolver resolver, ILibraryManager libraryManager, Func<PluginConfiguration> getConfiguration)
        {
            _resolver = resolver;
            _libraryManager = libraryManager;
            _getConfiguration = getConfiguration;
        }

        public string Name => Constants.PluginName;

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var video = await ResolveVideoAsync(info, cancellationToken).ConfigureAwait(false);
            if (video == null)
            {
                return new MetadataResult<Episode>();
            }

            var result = Utils.VideoToEpisode(video);
            if (result.Item.PremiereDate.HasValue)
            {
                var channelId = GetKnownChannelId(info.SeriesProviderIds) ?? video.Snippet?.ChannelId;
                var siblings = GetSiblingPremiereDates(channelId, video.Id);
                result.Item.ParentIndexNumber = 1;
                result.Item.IndexNumber = Utils.ComputeEpisodeIndex(video.Id, result.Item.PremiereDate.Value, siblings);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var limit = GetSearchResultLimit();
            var found = new List<Video>();

            // An ID (or pasted video URL) in the "YouTube" field, in the Name box, or in the file
            // name is exact: fetch it and list it first.
            var videoId = GetKnownVideoId(searchInfo);
            var nameIsVideoUrl = false;
            if (string.IsNullOrEmpty(videoId) && LooksLikeUrl(searchInfo.Name))
            {
                videoId = Utils.ParseVideoReference(searchInfo.Name);
                nameIsVideoUrl = !string.IsNullOrEmpty(videoId);
            }

            if (!string.IsNullOrEmpty(videoId))
            {
                AddDistinct(found, await _resolver.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false));
            }

            // A typed Name is always searched (so it is never silently ignored); with nothing typed
            // and no ID match, fall back to our own parse of the file name - Jellyfin's episode
            // parser expects "S01E01"-style names and mangles YouTube titles.
            var query = nameIsVideoUrl ? null : searchInfo.Name;
            if (string.IsNullOrWhiteSpace(query) && found.Count == 0)
            {
                query = Utils.ParseEpisodeFileName(searchInfo.Path).Title;
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                // Scope to the channel when it's known; a manual search with no known channel is
                // still useful (the user picks the right hit), so fall through to a site-wide search.
                var channelId = await ResolveChannelIdAsync(searchInfo, cancellationToken).ConfigureAwait(false);
                var matches = await _resolver.SearchVideosAsync(query, channelId, limit, cancellationToken).ConfigureAwait(false);
                foreach (var match in matches)
                {
                    AddDistinct(found, match);
                }
            }

            return found.Take(limit).Select(Utils.VideoToSearchResult).ToList();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }

        private async Task<Video?> ResolveVideoAsync(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var videoId = GetKnownVideoId(info);
            if (!string.IsNullOrEmpty(videoId))
            {
                return await _resolver.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
            }

            if (!_getConfiguration().EnableTitleSearchFallback)
            {
                return null;
            }

            var (title, uploadDate) = Utils.ParseEpisodeFileName(info.Path);
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // Automatic (unattended) matching is only attempted inside the file's own channel: an
            // exact title match across all of YouTube is not strong enough evidence on its own.
            var channelId = await ResolveChannelIdAsync(info, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelId))
            {
                return null;
            }

            return await _resolver.FindVideoByTitleAsync(title, channelId, uploadDate, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The channel a file belongs to: the series' stored provider ID when Jellyfin passes one,
        /// otherwise a channel-name lookup on the parent folder (same rules as the series provider).
        /// </summary>
        private async Task<string?> ResolveChannelIdAsync(EpisodeInfo info, CancellationToken cancellationToken)
        {
            // The series' stored value is normally a channel ID, but can be a handle/name/URL the
            // user typed into the series' "YouTube" field that hasn't been refreshed into an ID yet.
            var stored = Utils.ParseChannelReference(Utils.GetStoredProviderValue(info.SeriesProviderIds));
            if (stored.Kind == ChannelReferenceKind.Id)
            {
                return stored.Value;
            }

            if (stored.Kind is ChannelReferenceKind.Handle or ChannelReferenceKind.Name)
            {
                var explicitMatch = await _resolver.FindChannelByNameAsync(stored.Value, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(explicitMatch?.Id))
                {
                    return explicitMatch!.Id;
                }
            }

            var parentFolder = Utils.GetParentPath(info.Path);
            var fromFolder = Utils.GetChannelId(Utils.GetLastPathSegment(parentFolder ?? string.Empty));
            if (!string.IsNullOrEmpty(fromFolder))
            {
                return fromFolder;
            }

            if (!_getConfiguration().EnableChannelNameSearch)
            {
                return null;
            }

            var channelName = Utils.GetChannelNameFromPath(parentFolder);
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return null;
            }

            var channel = await _resolver.FindChannelByNameAsync(channelName, cancellationToken).ConfigureAwait(false);
            return channel?.Id;
        }

        private static string? GetKnownVideoId(EpisodeInfo info)
        {
            // Accepts a bare ID or a pasted video URL; anything else stored there is ignored.
            var stored = Utils.ParseVideoReference(Utils.GetStoredProviderValue(info.ProviderIds));
            if (!string.IsNullOrEmpty(stored))
            {
                return stored;
            }

            // Only the file name itself: the parent folder may carry a "[channelId]".
            var fromPath = Utils.GetVideoId(Utils.GetLastPathSegment(info.Path ?? string.Empty));
            return string.IsNullOrEmpty(fromPath) ? null : fromPath;
        }

        private static string? GetKnownChannelId(IReadOnlyDictionary<string, string>? seriesProviderIds)
        {
            var stored = Utils.ParseChannelReference(Utils.GetStoredProviderValue(seriesProviderIds));
            return stored.Kind == ChannelReferenceKind.Id ? stored.Value : null;
        }

        private static bool LooksLikeUrl(string? text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (text.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("youtu.be", StringComparison.OrdinalIgnoreCase));
        }

        private static void AddDistinct(List<Video> videos, Video? video)
        {
            if (video != null && !string.IsNullOrEmpty(video.Id) && videos.All(v => v.Id != video.Id))
            {
                videos.Add(video);
            }
        }

        private int GetSearchResultLimit()
        {
            var limit = _getConfiguration().SearchResultLimit;
            return Math.Clamp(limit <= 0 ? 10 : limit, 1, Constants.MaxIdsPerListRequest);
        }

        /// <summary>
        /// Looks up the channel's Series item by its stored provider ID, then returns the premiere
        /// dates of its other already-scanned Episode children (excluding the current video) — so
        /// every video in the channel can be assigned a distinct <see cref="Episode.IndexNumber"/>
        /// and none of them collapse into "alternate versions" of the same episode.
        /// </summary>
        private IEnumerable<(string VideoId, DateTime PremiereDate)> GetSiblingPremiereDates(string? channelId, string currentVideoId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                return Array.Empty<(string, DateTime)>();
            }

            var series = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                HasAnyProviderId = new Dictionary<string, string> { { Constants.PluginName, channelId } }
            }).FirstOrDefault();

            if (series == null)
            {
                return Array.Empty<(string, DateTime)>();
            }

            return _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    AncestorIds = new[] { series.Id }
                })
                .Where(e => e.PremiereDate.HasValue
                    && e.ProviderIds.TryGetValue(Constants.PluginName, out var id)
                    && id != currentVideoId)
                .Select(e => (e.ProviderIds[Constants.PluginName], e.PremiereDate!.Value))
                .ToList();
        }
    }
}
