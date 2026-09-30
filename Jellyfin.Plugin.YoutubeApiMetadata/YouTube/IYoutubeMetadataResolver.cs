using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;

namespace Jellyfin.Plugin.YoutubeApiMetadata.YouTube
{
    /// <summary>
    /// Resolves videos and channels, checking the disk cache first and falling back to the
    /// YouTube Data API (saving the result back to the cache) on a miss. Shared by every provider
    /// that needs video/channel data - metadata providers and image providers alike.
    /// Every method that returns a <see cref="Channel"/> or <see cref="Video"/> returns the
    /// *full* resource (all parts the plugin maps), never a bare search snippet.
    /// </summary>
    public interface IYoutubeMetadataResolver
    {
        Task<Video?> GetVideoAsync(string videoId, CancellationToken cancellationToken);

        Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

        /// <summary>
        /// Resolves a channel from a name as a user (or yt-dlp) would write it in a folder name:
        /// either a handle ("@rickastley", exact 1-unit lookup) or a display name ("Rick Astley",
        /// 100-unit search, preferring an exact title match over YouTube's top hit). The resolved ID
        /// is cached against the normalized name so the search is only ever paid once per name.
        /// Returns null when nothing plausible is found.
        /// </summary>
        Task<Channel?> FindChannelByNameAsync(string name, CancellationToken cancellationToken);

        /// <summary>
        /// Searches channels for a free-text query and returns them as full channel resources
        /// (search snippets hydrated via one channels.list call), ordered with exact title/handle
        /// matches first and YouTube's relevance order after that.
        /// </summary>
        Task<IReadOnlyList<Channel>> SearchChannelsAsync(string query, int maxResults, CancellationToken cancellationToken);

        /// <summary>
        /// Searches videos for a free-text query, optionally within one channel, and returns them
        /// as full video resources (search snippets hydrated via one videos.list call), ordered with
        /// exact title matches first and YouTube's relevance order after that.
        /// </summary>
        Task<IReadOnlyList<Video>> SearchVideosAsync(string query, string? channelId, int maxResults, CancellationToken cancellationToken);

        /// <summary>
        /// Finds the video a local file most likely is, given the title parsed from its file name
        /// (and its upload date when the file name carries one), searching within the channel when
        /// one is known. Only returns a video the plugin is confident about: an exact (normalized)
        /// title match, or the top hit when it was published on the file's upload date.
        /// </summary>
        Task<Video?> FindVideoByTitleAsync(string title, string? channelId, DateTime? uploadDate, CancellationToken cancellationToken);
    }
}
