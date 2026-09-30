using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;

namespace Jellyfin.Plugin.YoutubeApiMetadata.YouTube
{
    /// <summary>
    /// Thin wrapper around the YouTube Data API v3, used by the metadata providers.
    /// Quota costs (per call, out of the default 10 000 units/day): every *.list by ID costs 1 unit
    /// regardless of how many IDs (up to 50) it carries; search.list costs 100 units.
    /// </summary>
    public interface IYouTubeApiClient
    {
        /// <summary>
        /// Fetches a single video by ID with every part the plugin maps (snippet, contentDetails,
        /// statistics, topicDetails, status, liveStreamingDetails).
        /// Returns null if no video with that ID exists (deleted/private/invalid ID).
        /// </summary>
        Task<Video?> GetVideoAsync(string videoId, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches up to 50 videos in one request (1 quota unit). Unknown IDs are simply absent
        /// from the result; order is not guaranteed to match <paramref name="videoIds"/>.
        /// </summary>
        Task<IReadOnlyList<Video>> GetVideosAsync(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches a single channel by ID with every part the plugin maps (snippet,
        /// brandingSettings, statistics, topicDetails, contentDetails, status).
        /// Returns null if no channel with that ID exists.
        /// </summary>
        Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches up to 50 channels in one request (1 quota unit). Unknown IDs are simply absent
        /// from the result; order is not guaranteed to match <paramref name="channelIds"/>.
        /// </summary>
        Task<IReadOnlyList<Channel>> GetChannelsAsync(IReadOnlyCollection<string> channelIds, CancellationToken cancellationToken);

        /// <summary>
        /// Resolves a channel by its handle ("@name", with or without the leading "@") via
        /// channels.list?forHandle — an exact lookup that costs 1 quota unit instead of the
        /// 100 a search does. Returns null if no channel owns that handle.
        /// </summary>
        Task<Channel?> GetChannelByHandleAsync(string handle, CancellationToken cancellationToken);

        /// <summary>
        /// Searches for videos matching a free-text query, optionally restricted to a single
        /// channel (used by Jellyfin's manual "Identify" and by the file-name → video fallback).
        /// Costs 100 quota units.
        /// </summary>
        Task<IReadOnlyList<SearchResult>> SearchVideosAsync(string query, string? channelId, int maxResults, CancellationToken cancellationToken);

        /// <summary>
        /// Searches for channels matching a free-text query (used by Jellyfin's manual "Identify"
        /// and by the folder-name → channel fallback). Costs 100 quota units.
        /// </summary>
        Task<IReadOnlyList<SearchResult>> SearchChannelsAsync(string query, int maxResults, CancellationToken cancellationToken);
    }
}
