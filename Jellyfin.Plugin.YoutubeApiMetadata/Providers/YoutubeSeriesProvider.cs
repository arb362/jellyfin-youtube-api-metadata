using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Providers
{
    /// <summary>
    /// Fetches Series metadata (a YouTube channel = a "show") from the YouTube Data API v3.
    /// A series folder can be named "Channel Name [channelId]" (24-char channel ID between square
    /// brackets, same convention as episode files) to resolve without a search. Folders following
    /// yt-dlp's plain "%(uploader)s" convention (no ID) — or named after the channel's handle,
    /// "@rickastley" — fall back to a name lookup on first import; the resolved channel ID is then
    /// persisted to ProviderIds so later scans skip the lookup.
    /// </summary>
    public class YoutubeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
    {
        private readonly IYoutubeMetadataResolver _resolver;
        private readonly Func<PluginConfiguration> _getConfiguration;

        public YoutubeSeriesProvider(IYoutubeMetadataResolver resolver, Func<PluginConfiguration> getConfiguration)
        {
            _resolver = resolver;
            _getConfiguration = getConfiguration;
        }

        public string Name => Constants.PluginName;

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var channel = await ResolveChannelAsync(info, cancellationToken).ConfigureAwait(false);
            return channel == null ? new MetadataResult<Series>() : Utils.ChannelToSeries(channel);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var limit = GetSearchResultLimit();
            var found = new List<Channel>();
            var queries = new List<string>();

            // The Identify dialog has two places to type: the "YouTube" ID field (arrives in
            // ProviderIds) and the Name box. People put IDs, handles, names and pasted URLs in
            // either one, so both are parsed the same way: an ID is fetched directly, anything
            // else becomes a search.
            foreach (var input in new[] { Utils.GetStoredProviderValue(searchInfo.ProviderIds), searchInfo.Name })
            {
                var reference = Utils.ParseChannelReference(input);
                if (reference.Kind == ChannelReferenceKind.Id)
                {
                    AddDistinct(found, await _resolver.GetChannelAsync(reference.Value, cancellationToken).ConfigureAwait(false));
                }
                else if (reference.Kind != ChannelReferenceKind.None
                    && !queries.Contains(reference.Value, StringComparer.OrdinalIgnoreCase))
                {
                    queries.Add(reference.Value);
                }
            }

            // Nothing typed at all: fall back to what the folder says.
            if (found.Count == 0 && queries.Count == 0)
            {
                var folderId = Utils.GetChannelId(Utils.GetLastPathSegment(searchInfo.Path ?? string.Empty));
                if (!string.IsNullOrEmpty(folderId))
                {
                    AddDistinct(found, await _resolver.GetChannelAsync(folderId, cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    var folderName = Utils.GetChannelNameFromPath(searchInfo.Path);
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        queries.Add(folderName);
                    }
                }
            }

            foreach (var query in queries)
            {
                if (found.Count >= limit)
                {
                    break;
                }

                var matches = await _resolver.SearchChannelsAsync(query, limit, cancellationToken).ConfigureAwait(false);
                foreach (var match in matches)
                {
                    AddDistinct(found, match);
                }
            }

            return found.Take(limit).Select(Utils.ChannelToSearchResult).ToList();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }

        private Task<Channel?> ResolveChannelAsync(SeriesInfo info, CancellationToken cancellationToken)
        {
            return ChannelLookup.ResolveAsync(
                _resolver,
                info.ProviderIds,
                info.Path,
                info.Name,
                _getConfiguration().EnableChannelNameSearch,
                cancellationToken);
        }

        private static void AddDistinct(List<Channel> channels, Channel? channel)
        {
            if (channel != null && !string.IsNullOrEmpty(channel.Id) && channels.All(c => c.Id != channel.Id))
            {
                channels.Add(channel);
            }
        }

        private int GetSearchResultLimit()
        {
            var limit = _getConfiguration().SearchResultLimit;
            return Math.Clamp(limit <= 0 ? 10 : limit, 1, Constants.MaxIdsPerListRequest);
        }
    }
}
