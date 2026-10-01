using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Providers
{
    /// <summary>
    /// Supplies the video thumbnail as the Primary image for an Episode. The video is located by
    /// its stored provider ID first (set by the metadata provider, including when it was resolved
    /// through a title search), then by the "[videoId]" in the file name.
    /// </summary>
    public class YoutubeEpisodeImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IYoutubeMetadataResolver _resolver;

        public YoutubeEpisodeImageProvider(IYoutubeMetadataResolver resolver)
        {
            _resolver = resolver;
        }

        public string Name => Constants.PluginName;

        public int Order => 1;

        public bool Supports(BaseItem item) => item is Episode;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var videoId = Utils.ParseVideoReference(Utils.GetStoredProviderValue(item.ProviderIds))
                ?? Utils.GetVideoId(Utils.GetLastPathSegment(item.Path ?? string.Empty));

            if (string.IsNullOrEmpty(videoId))
            {
                return System.Array.Empty<RemoteImageInfo>();
            }

            var video = await _resolver.GetVideoAsync(videoId, cancellationToken).ConfigureAwait(false);
            var url = video == null ? null : Utils.GetBestThumbnailUrl(video.Snippet?.Thumbnails);
            if (string.IsNullOrEmpty(url))
            {
                return System.Array.Empty<RemoteImageInfo>();
            }

            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = url,
                    Type = ImageType.Primary
                }
            };
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }
    }
}
