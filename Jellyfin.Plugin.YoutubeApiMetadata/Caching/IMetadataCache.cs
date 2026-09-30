using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Caching
{
    /// <summary>
    /// Disk cache for YouTube Data API responses, so repeated Jellyfin scans don't burn API quota.
    /// </summary>
    public interface IMetadataCache
    {
        /// <summary>
        /// Returns the cached video, or null if there is no fresh cache entry for it.
        /// </summary>
        Task<Video?> GetVideoAsync(string videoId, CancellationToken cancellationToken);

        Task SaveVideoAsync(string videoId, Video video, CancellationToken cancellationToken);

        /// <summary>
        /// Returns the cached channel, or null if there is no fresh cache entry for it.
        /// </summary>
        Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken);

        Task SaveChannelAsync(string channelId, Channel channel, CancellationToken cancellationToken);

        /// <summary>
        /// Returns the channel ID previously resolved for a channel name/handle (as normalized by
        /// <see cref="Utils.NormalizeName"/>), or null if there is no fresh entry. Lets a folder
        /// whose name has no embedded ID be re-resolved without repeating a 100-unit search.
        /// </summary>
        Task<string?> GetChannelIdForNameAsync(string normalizedName, CancellationToken cancellationToken);

        Task SaveChannelIdForNameAsync(string normalizedName, string channelId, CancellationToken cancellationToken);
    }
}
