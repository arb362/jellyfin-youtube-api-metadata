using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.Providers;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.Providers
{
    public class YoutubeSeriesImageProviderTests
    {
        private const string ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw";

        private static readonly Channel ChannelWithBanner = new()
        {
            Id = ChannelId,
            Snippet = new ChannelSnippet
            {
                Title = "Rick Astley",
                Thumbnails = new ThumbnailDetails { High = new Thumbnail { Url = "https://example.com/avatar.jpg" } }
            },
            BrandingSettings = new ChannelBrandingSettings
            {
                Image = new ImageSettings { BannerExternalUrl = "https://yt3.googleusercontent.com/banner" }
            }
        };

        private static Func<PluginConfiguration> Config(bool nameSearch = true)
            => () => new PluginConfiguration { EnableChannelNameSearch = nameSearch };

        [Fact]
        public async Task GetImages_ReturnsAvatarAndBanners_WhenChannelIdKnownFromProviderIds()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(ChannelWithBanner);

            var provider = new YoutubeSeriesImageProvider(resolver.Object, Config());
            var item = new Series
            {
                Path = "/media/Rick Astley",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var images = (await provider.GetImages(item, CancellationToken.None)).ToList();

            Assert.Equal(3, images.Count);
            Assert.Equal("https://example.com/avatar.jpg", images.Single(i => i.Type == ImageType.Primary).Url);
            Assert.Equal("https://yt3.googleusercontent.com/banner" + Constants.BannerBackdropSuffix, images.Single(i => i.Type == ImageType.Backdrop).Url);
            Assert.Equal("https://yt3.googleusercontent.com/banner" + Constants.BannerSuffix, images.Single(i => i.Type == ImageType.Banner).Url);
            Assert.All(images, i => Assert.Equal(Constants.PluginName, i.ProviderName));
        }

        [Fact]
        public async Task GetImages_ReturnsOnlyAvatar_WhenChannelHasNoBanner()
        {
            var channel = new Channel
            {
                Id = ChannelId,
                Snippet = new ChannelSnippet { Thumbnails = new ThumbnailDetails { Default__ = new Thumbnail { Url = "https://example.com/default.jpg" } } }
            };
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(channel);

            var provider = new YoutubeSeriesImageProvider(resolver.Object, Config());
            var item = new Series { Path = $"/media/Rick Astley [{ChannelId}]" };

            var images = (await provider.GetImages(item, CancellationToken.None)).ToList();

            var single = Assert.Single(images);
            Assert.Equal(ImageType.Primary, single.Type);
        }

        [Fact]
        public async Task GetImages_FallsBackToFolderNameLookup_WhenNoChannelIdResolvable()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("Rick Astley", It.IsAny<CancellationToken>())).ReturnsAsync(ChannelWithBanner);

            var provider = new YoutubeSeriesImageProvider(resolver.Object, Config());
            var item = new Series { Path = "/media/Rick Astley", Name = "Rick Astley" };

            var images = (await provider.GetImages(item, CancellationToken.None)).ToList();

            Assert.Equal(3, images.Count);
        }

        [Fact]
        public async Task GetImages_ReturnsEmpty_WhenNoChannelIdResolvable_AndNameSearchDisabled()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            var provider = new YoutubeSeriesImageProvider(resolver.Object, Config(nameSearch: false));
            var item = new Series { Path = "/media/Some Random Folder", Name = "Some Random Folder" };

            var images = await provider.GetImages(item, CancellationToken.None);

            Assert.Empty(images);
            resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public void SupportsPrimaryBackdropAndBanner()
        {
            var provider = new YoutubeSeriesImageProvider(Mock.Of<IYoutubeMetadataResolver>(), Config());

            var supported = provider.GetSupportedImages(new Series()).ToList();

            Assert.Contains(ImageType.Primary, supported);
            Assert.Contains(ImageType.Backdrop, supported);
            Assert.Contains(ImageType.Banner, supported);
            Assert.True(provider.Supports(new Series()));
            Assert.False(provider.Supports(new Episode()));
        }
    }
}
