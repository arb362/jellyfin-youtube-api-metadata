using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Providers;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities.TV;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.Providers
{
    public class YoutubeEpisodeImageProviderTests
    {
        private static readonly Video SampleVideo = new()
        {
            Id = "dQw4w9WgXcQ",
            Snippet = new VideoSnippet
            {
                Title = "Never Gonna Give You Up",
                Thumbnails = new ThumbnailDetails { High = new Thumbnail { Url = "https://example.com/high.jpg" } }
            }
        };

        [Fact]
        public async Task GetImages_ReturnsThumbnail_WhenVideoResolves()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>();
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = new YoutubeEpisodeImageProvider(resolver.Object);
            var item = new Episode { Path = "/media/Rick Astley/Rick Astley - 20091025 - Title [dQw4w9WgXcQ].mkv" };

            var images = (await provider.GetImages(item, CancellationToken.None)).ToList();

            Assert.Single(images);
            Assert.Equal("https://example.com/high.jpg", images[0].Url);
        }

        [Fact]
        public async Task GetImages_UsesStoredProviderId_WhenFileNameHasNoId()
        {
            // A file matched through the title-search fallback has no ID in its name, but the
            // metadata provider stored the resolved ID on the item before images are refreshed.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = new YoutubeEpisodeImageProvider(resolver.Object);
            var item = new Episode
            {
                Path = "/media/Rick Astley/20091025 - Never Gonna Give You Up.mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, "dQw4w9WgXcQ" } }
            };

            var images = (await provider.GetImages(item, CancellationToken.None)).ToList();

            Assert.Single(images);
        }

        [Fact]
        public async Task GetImages_ReturnsEmpty_WhenFileNameHasNoId()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            var provider = new YoutubeEpisodeImageProvider(resolver.Object);
            var item = new Episode { Path = "/media/Rick Astley/no id here.mkv" };

            var images = await provider.GetImages(item, CancellationToken.None);

            Assert.Empty(images);
            resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public void Supports_OnlyEpisodes()
        {
            var provider = new YoutubeEpisodeImageProvider(Mock.Of<IYoutubeMetadataResolver>());

            Assert.True(provider.Supports(new Episode()));
            Assert.False(provider.Supports(new Series()));
        }
    }
}
