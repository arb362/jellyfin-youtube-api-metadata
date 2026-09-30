using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Providers
{
    /// <summary>
    /// Supplies the channel avatar as the Primary image, and the channel banner as the Backdrop
    /// and Banner images, for a Series.
    /// </summary>
    public class YoutubeSeriesImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IYoutubeMetadataResolver _resolver;
        private readonly Func<PluginConfiguration> _getConfiguration;

        public YoutubeSeriesImageProvider(IYoutubeMetadataResolver resolver, Func<PluginConfiguration> getConfiguration)
        {
            _resolver = resolver;
            _getConfiguration = getConfiguration;
        }

        public string Name => Constants.PluginName;

        public int Order => 1;

        public bool Supports(BaseItem item) => item is Series;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var channel = await ResolveChannelAsync(item, cancellationToken).ConfigureAwait(false);
            if (channel == null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var images = new List<RemoteImageInfo>();

            var avatar = Utils.GetBestThumbnailUrl(channel.Snippet?.Thumbnails);
            if (!string.IsNullOrEmpty(avatar))
            {
                images.Add(new RemoteImageInfo { ProviderName = Name, Url = avatar, Type = ImageType.Primary });
            }

            var backdrop = Utils.GetBannerUrl(channel, Constants.BannerBackdropSuffix);
            if (!string.IsNullOrEmpty(backdrop))
            {
                images.Add(new RemoteImageInfo { ProviderName = Name, Url = backdrop, Type = ImageType.Backdrop, Width = 2560, Height = 1440 });
            }

            var banner = Utils.GetBannerUrl(channel, Constants.BannerSuffix);
            if (!string.IsNullOrEmpty(banner))
            {
                images.Add(new RemoteImageInfo { ProviderName = Name, Url = banner, Type = ImageType.Banner, Width = 2120, Height = 1192 });
            }

            return images;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }

        private async Task<Channel?> ResolveChannelAsync(BaseItem item, CancellationToken cancellationToken)
        {
            var channelId = Utils.ResolveChannelId(item.ProviderIds, item.Path, item.Name);
            if (!string.IsNullOrEmpty(channelId))
            {
                return await _resolver.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            }

            if (!_getConfiguration().EnableChannelNameSearch)
            {
                return null;
            }

            var name = Utils.GetChannelNameFromPath(item.Path) ?? item.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return await _resolver.FindChannelByNameAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }
}
