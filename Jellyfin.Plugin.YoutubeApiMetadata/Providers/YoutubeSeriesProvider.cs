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
            var channelId = Utils.ResolveChannelId(searchInfo.ProviderIds, searchInfo.Path, null);
            if (!string.IsNullOrEmpty(channelId))
            {
                var channel = await _resolver.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
                return channel == null ? Array.Empty<RemoteSearchResult>() : new[] { Utils.ChannelToSearchResult(channel) };
            }

            // In the Identify dialog, Name is whatever the user typed; on an automatic lookup it is
            // Jellyfin's parsed folder name. Either way it's the channel name (or handle) to search.
            var query = string.IsNullOrWhiteSpace(searchInfo.Name)
                ? Utils.GetChannelNameFromPath(searchInfo.Path)
                : searchInfo.Name;

            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<RemoteSearchResult>();
            }

            var matches = await _resolver.SearchChannelsAsync(query, GetSearchResultLimit(), cancellationToken).ConfigureAwait(false);
            return matches
                .Where(c => !string.IsNullOrEmpty(c.Id))
                .Select(Utils.ChannelToSearchResult)
                .ToList();
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }

        private async Task<Channel?> ResolveChannelAsync(SeriesInfo info, CancellationToken cancellationToken)
        {
            var channelId = Utils.ResolveChannelId(info.ProviderIds, info.Path, info.Name);
            if (!string.IsNullOrEmpty(channelId))
            {
                return await _resolver.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            }

            if (!_getConfiguration().EnableChannelNameSearch)
            {
                return null;
            }

            // Prefer the raw folder name over Jellyfin's parsed Name: the parser can strip pieces a
            // channel name legitimately contains (a trailing year in parentheses, for instance).
            var name = Utils.GetChannelNameFromPath(info.Path) ?? info.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return await _resolver.FindChannelByNameAsync(name, cancellationToken).ConfigureAwait(false);
        }

        private int GetSearchResultLimit()
        {
            var limit = _getConfiguration().SearchResultLimit;
            return Math.Clamp(limit <= 0 ? 10 : limit, 1, Constants.MaxIdsPerListRequest);
        }
    }
}
