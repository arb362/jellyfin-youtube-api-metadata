using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.YoutubeApiMetadata.YouTube
{
    /// <inheritdoc cref="IYoutubeMetadataResolver" />
    public sealed class YoutubeMetadataResolver : IYoutubeMetadataResolver
    {
        /// <summary>
        /// How many search hits to consider when resolving a single channel/video from a name.
        /// YouTube's top hit is usually right, but for generic names ("Music", "News") an exact
        /// title match a few places down is a far safer pick than the most popular near-match.
        /// </summary>
        private const int SingleMatchCandidates = 5;

        private readonly IYouTubeApiClient _client;
        private readonly IMetadataCache _cache;
        private readonly ILogger<YoutubeMetadataResolver> _logger;

        public YoutubeMetadataResolver(IYouTubeApiClient client, IMetadataCache cache, ILogger<YoutubeMetadataResolver>? logger = null)
        {
            _client = client;
            _cache = cache;
            _logger = logger ?? NullLogger<YoutubeMetadataResolver>.Instance;
        }

        public async Task<Video?> GetVideoAsync(string videoId, CancellationToken cancellationToken)
        {
            var cached = await _cache.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }

            var video = await _client.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (video != null)
            {
                await _cache.SaveVideoAsync(videoId, video, cancellationToken).ConfigureAwait(false);
            }

            return video;
        }

        public async Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken)
        {
            var cached = await _cache.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }

            var channel = await _client.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            if (channel != null)
            {
                await _cache.SaveChannelAsync(channelId, channel, cancellationToken).ConfigureAwait(false);
            }

            return channel;
        }

        public async Task<Channel?> FindChannelByNameAsync(string name, CancellationToken cancellationToken)
        {
            var normalizedName = Utils.NormalizeName(name);
            if (string.IsNullOrEmpty(normalizedName))
            {
                return null;
            }

            var cachedId = await _cache.GetChannelIdForNameAsync(normalizedName, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cachedId))
            {
                var cachedChannel = await GetChannelAsync(cachedId, cancellationToken).ConfigureAwait(false);
                if (cachedChannel != null)
                {
                    return cachedChannel;
                }
            }

            Channel? channel = null;
            if (Utils.IsHandle(name))
            {
                channel = await _client.GetChannelByHandleAsync(name.Trim(), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("YouTube handle lookup for {Name}: {Result}", name, channel == null ? "no channel" : channel.Id);
            }

            if (channel == null)
            {
                var candidates = await _client.SearchChannelsAsync(name.Trim(), SingleMatchCandidates, cancellationToken).ConfigureAwait(false);
                var best = Utils.PickBestChannelMatch(name, candidates);
                _logger.LogInformation(
                    "YouTube channel search for {Name}: {Count} hit(s), picked {Picked}",
                    name,
                    candidates.Count,
                    best == null ? "none" : $"{best.Snippet?.Title} ({best.Id?.ChannelId})");
                var bestId = best?.Id?.ChannelId;
                if (!string.IsNullOrEmpty(bestId))
                {
                    channel = await GetChannelAsync(bestId, cancellationToken).ConfigureAwait(false);
                }
            }

            if (channel == null || string.IsNullOrEmpty(channel.Id))
            {
                _logger.LogWarning("Could not resolve a YouTube channel for {Name}", name);
                return null;
            }

            await _cache.SaveChannelAsync(channel.Id, channel, cancellationToken).ConfigureAwait(false);
            await _cache.SaveChannelIdForNameAsync(normalizedName, channel.Id, cancellationToken).ConfigureAwait(false);
            return channel;
        }

        public async Task<IReadOnlyList<Channel>> SearchChannelsAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            {
                return Array.Empty<Channel>();
            }

            var results = new List<Channel>();

            // A handle is an exact identifier: resolve it directly (1 unit) and put it first, then
            // still run the text search so near-misses show up in the Identify dialog too.
            if (Utils.IsHandle(query))
            {
                var byHandle = await _client.GetChannelByHandleAsync(query.Trim(), cancellationToken).ConfigureAwait(false);
                if (byHandle != null)
                {
                    results.Add(byHandle);
                }
            }

            var hits = await _client.SearchChannelsAsync(query.Trim(), maxResults, cancellationToken).ConfigureAwait(false);
            var ids = hits
                .Select(h => h.Id?.ChannelId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Where(id => results.All(r => r.Id != id))
                .ToList();

            if (ids.Count > 0)
            {
                var channels = await _client.GetChannelsAsync(ids, cancellationToken).ConfigureAwait(false);
                var byId = channels.Where(c => !string.IsNullOrEmpty(c.Id)).ToDictionary(c => c.Id, StringComparer.Ordinal);

                // Preserve YouTube's relevance order (channels.list doesn't guarantee input order).
                foreach (var id in ids)
                {
                    if (byId.TryGetValue(id, out var channel))
                    {
                        results.Add(channel);
                    }
                }
            }

            foreach (var channel in results)
            {
                await _cache.SaveChannelAsync(channel.Id, channel, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("YouTube channel search for {Query}: {Hits} hit(s), {Returned} returned", query, hits.Count, results.Count);
            return Utils.OrderChannelsByMatch(query, results).Take(maxResults).ToList();
        }

        public async Task<IReadOnlyList<Video>> SearchVideosAsync(string query, string? channelId, int maxResults, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            {
                return Array.Empty<Video>();
            }

            var hits = await _client.SearchVideosAsync(query.Trim(), channelId, maxResults, cancellationToken).ConfigureAwait(false);
            var ids = hits
                .Select(h => h.Id?.VideoId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            _logger.LogInformation("YouTube video search for {Query} in channel {ChannelId}: {Hits} hit(s)", query, channelId ?? "(any)", ids.Count);
            if (ids.Count == 0)
            {
                return Array.Empty<Video>();
            }

            var videos = await _client.GetVideosAsync(ids, cancellationToken).ConfigureAwait(false);
            var byId = videos.Where(v => !string.IsNullOrEmpty(v.Id)).ToDictionary(v => v.Id, StringComparer.Ordinal);

            var ordered = new List<Video>();
            foreach (var id in ids)
            {
                if (byId.TryGetValue(id, out var video))
                {
                    ordered.Add(video);
                    await _cache.SaveVideoAsync(id, video, cancellationToken).ConfigureAwait(false);
                }
            }

            return Utils.OrderVideosByMatch(query, ordered).ToList();
        }

        public async Task<Video?> FindVideoByTitleAsync(string title, string? channelId, DateTime? uploadDate, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var candidates = await SearchVideosAsync(title, channelId, SingleMatchCandidates, cancellationToken).ConfigureAwait(false);
            return Utils.PickBestVideoMatch(title, uploadDate, candidates);
        }
    }
}
