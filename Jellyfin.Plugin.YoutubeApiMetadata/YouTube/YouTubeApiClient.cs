using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Jellyfin.Plugin.YoutubeApiMetadata.YouTube
{
    /// <inheritdoc cref="IYouTubeApiClient" />
    public sealed class YouTubeApiClient : IYouTubeApiClient, IDisposable
    {
        // Every part the API exposes to an API-key (non-OAuth) caller that carries data the plugin
        // can map onto Jellyfin items. Parts are free: a *.list call costs 1 unit no matter how many
        // parts are requested, so there is no quota reason to ask for less.
        private const string VideoParts = "snippet,contentDetails,statistics,topicDetails,status,liveStreamingDetails";
        private const string ChannelParts = "snippet,brandingSettings,statistics,topicDetails,contentDetails,status";
        private const string SearchParts = "snippet";

        private readonly Func<string> _getApiKey;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly object _serviceLock = new();
        private YouTubeService? _service;
        private string? _serviceApiKey;

        /// <summary>
        /// Creates a client that reads the API key through <paramref name="getApiKey"/> on every
        /// call. Jellyfin constructs providers (and therefore this client) once at server startup,
        /// typically before the admin has entered a key; reading the key lazily means a key saved
        /// (or changed) in the settings page takes effect immediately, without a restart.
        /// </summary>
        public YouTubeApiClient(Func<string> getApiKey, IHttpClientFactory? httpClientFactory = null)
        {
            _getApiKey = getApiKey;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Creates a client with a fixed API key (tests and one-off use).
        /// </summary>
        public YouTubeApiClient(string apiKey, IHttpClientFactory? httpClientFactory = null)
            : this(() => apiKey, httpClientFactory)
        {
        }

        public async Task<Video?> GetVideoAsync(string videoId, CancellationToken cancellationToken)
        {
            var videos = await GetVideosAsync(new[] { videoId }, cancellationToken).ConfigureAwait(false);
            return videos.FirstOrDefault();
        }

        public async Task<IReadOnlyList<Video>> GetVideosAsync(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken)
        {
            var results = new List<Video>();
            foreach (var chunk in Chunk(videoIds))
            {
                var request = GetService().Videos.List(VideoParts);
                request.Id = chunk;
                var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (response.Items != null)
                {
                    results.AddRange(response.Items);
                }
            }

            return results;
        }

        public async Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken)
        {
            var channels = await GetChannelsAsync(new[] { channelId }, cancellationToken).ConfigureAwait(false);
            return channels.FirstOrDefault();
        }

        public async Task<IReadOnlyList<Channel>> GetChannelsAsync(IReadOnlyCollection<string> channelIds, CancellationToken cancellationToken)
        {
            var results = new List<Channel>();
            foreach (var chunk in Chunk(channelIds))
            {
                var request = GetService().Channels.List(ChannelParts);
                request.Id = chunk;
                var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (response.Items != null)
                {
                    results.AddRange(response.Items);
                }
            }

            return results;
        }

        public async Task<Channel?> GetChannelByHandleAsync(string handle, CancellationToken cancellationToken)
        {
            var normalized = handle.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            // The API accepts the handle with or without "@"; be consistent and always send it with.
            if (normalized[0] != '@')
            {
                normalized = "@" + normalized;
            }

            var request = GetService().Channels.List(ChannelParts);
            request.ForHandle = normalized;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return response.Items?.FirstOrDefault();
        }

        public async Task<IReadOnlyList<SearchResult>> SearchVideosAsync(string query, string? channelId, int maxResults, CancellationToken cancellationToken)
        {
            var request = GetService().Search.List(SearchParts);
            request.Q = query;
            request.Type = "video";
            request.MaxResults = maxResults;
            if (!string.IsNullOrEmpty(channelId))
            {
                request.ChannelId = channelId;
            }

            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return (IReadOnlyList<SearchResult>?)response.Items ?? Array.Empty<SearchResult>();
        }

        public async Task<IReadOnlyList<SearchResult>> SearchChannelsAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            var request = GetService().Search.List(SearchParts);
            request.Q = query;
            request.Type = "channel";
            request.MaxResults = maxResults;
            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return (IReadOnlyList<SearchResult>?)response.Items ?? Array.Empty<SearchResult>();
        }

        public void Dispose()
        {
            lock (_serviceLock)
            {
                _service?.Dispose();
                _service = null;
            }
        }

        /// <summary>
        /// Splits a set of IDs into the comma-joined batches the API accepts (max 50 per request),
        /// dropping blanks and duplicates.
        /// </summary>
        private static IEnumerable<string> Chunk(IEnumerable<string> ids)
        {
            var distinct = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < distinct.Count; i += Constants.MaxIdsPerListRequest)
            {
                yield return string.Join(",", distinct.Skip(i).Take(Constants.MaxIdsPerListRequest));
            }
        }

        private YouTubeService GetService()
        {
            var apiKey = (_getApiKey() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "No YouTube Data API v3 key configured. Set one in Dashboard > Plugins > YouTube API Metadata, save, then retry.");
            }

            lock (_serviceLock)
            {
                // Rebuild the service whenever the configured key changes (first use, or the admin
                // pasted a new key), so the change applies without restarting Jellyfin.
                if (_service != null && string.Equals(_serviceApiKey, apiKey, StringComparison.Ordinal))
                {
                    return _service;
                }

                _service?.Dispose();

                var initializer = new BaseClientService.Initializer
                {
                    ApiKey = apiKey,
                    ApplicationName = Constants.PluginName
                };

                if (_httpClientFactory != null)
                {
                    initializer.HttpClientFactory = _httpClientFactory;
                }

                _service = new YouTubeService(initializer);
                _serviceApiKey = apiKey;
                return _service;
            }
        }
    }
}
