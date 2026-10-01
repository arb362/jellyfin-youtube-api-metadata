using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.Providers;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Providers;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.Providers
{
    public class YoutubeSeriesProviderTests
    {
        private const string ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw";

        private static readonly Channel SampleChannel = new()
        {
            Id = ChannelId,
            Snippet = new ChannelSnippet
            {
                Title = "Rick Astley",
                Description = "Official channel.",
                PublishedAtDateTimeOffset = new DateTimeOffset(2006, 3, 14, 0, 0, 0, TimeSpan.Zero),
                Thumbnails = new ThumbnailDetails { High = new Thumbnail { Url = "https://example.com/avatar.jpg" } }
            }
        };

        private static Func<PluginConfiguration> Config(bool nameSearch = true, int limit = 10)
            => () => new PluginConfiguration { EnableChannelNameSearch = nameSearch, SearchResultLimit = limit };

        [Fact]
        public async Task GetMetadata_ResolvesChannelIdFromFolderName()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo { Path = $"/media/channels/Rick Astley [{ChannelId}]", Name = "Rick Astley" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Rick Astley", result.Item.Name);
            resolver.Verify(r => r.FindChannelByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetMetadata_PrefersStoredProviderIdOverFolderName()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo
            {
                Path = "/media/channels/Some Other Folder Name",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            resolver.Verify(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMetadata_FallsBackToNameSearch_WhenFolderHasNoId()
        {
            // Matches the real-world "%(uploader)s" folder naming used by yt-dlp/the old plugin:
            // no channel ID embedded anywhere, so the only way in is a name lookup.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("Rick Astley", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo { Path = "/media/channels/Rick Astley", Name = "Rick Astley" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Rick Astley", result.Item.Name);
            Assert.Equal(ChannelId, result.Item.ProviderIds[Constants.PluginName]);
        }

        [Fact]
        public async Task GetMetadata_SearchesByRawFolderName_NotJellyfinsParsedName()
        {
            // Jellyfin's series parser strips things like a trailing "(2019)"; the folder name is
            // the channel name exactly as the user/yt-dlp wrote it, so that's what gets searched.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("Some Channel (2019)", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo { Path = "/media/channels/Some Channel (2019)", Name = "Some Channel", Year = 2019 };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_PassesHandleFolderNameThrough()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("@RickAstleyYT", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo { Path = "/media/channels/@RickAstleyYT", Name = "@RickAstleyYT" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_ReturnsEmptyResult_WhenNameSearchFindsNothing()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Channel?)null);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var info = new SeriesInfo { Path = "/media/channels/Unknown Channel", Name = "Unknown Channel" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_SkipsNameSearch_WhenDisabledInConfiguration()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config(nameSearch: false));
            var info = new SeriesInfo { Path = "/media/channels/Rick Astley", Name = "Rick Astley" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
            resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetSearchResults_ReturnsHydratedChannel_WhenIdKnown()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var searchInfo = new SeriesInfo { ProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } } };

            var results = (await provider.GetSearchResults(searchInfo, CancellationToken.None)).ToList();

            var single = Assert.Single(results);
            Assert.Equal("Rick Astley", single.Name);
            Assert.Equal(2006, single.ProductionYear);
            Assert.Equal("https://example.com/avatar.jpg", single.ImageUrl);
        }

        [Fact]
        public async Task GetSearchResults_FallsBackToNameSearch_WhenNoIdAvailable()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchChannelsAsync("Rick Astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var searchInfo = new SeriesInfo { Name = "Rick Astley" };

            var results = (await provider.GetSearchResults(searchInfo, CancellationToken.None)).ToList();

            var single = Assert.Single(results);
            Assert.Equal(ChannelId, single.ProviderIds[Constants.PluginName]);
            Assert.Equal("Official channel.", single.Overview);
            Assert.Equal(Constants.PluginName, single.SearchProviderName);
        }

        [Theory]
        [InlineData("@FaithvilleProductions", "@FaithvilleProductions")]
        [InlineData("https://www.youtube.com/@FaithvilleProductions", "@FaithvilleProductions")]
        [InlineData("Faithville Productions", "Faithville Productions")]
        public async Task GetSearchResults_SearchesHandleOrNameTypedIntoTheIdField(string typed, string expectedQuery)
        {
            // Regression: the Identify dialog's "YouTube" field arrives in ProviderIds. A handle
            // typed there used to be sent to the API as a channel ID, which found nothing and
            // raised no error - an empty result list with a clean log.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchChannelsAsync(expectedQuery, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var searchInfo = new SeriesInfo { ProviderIds = new Dictionary<string, string> { { Constants.PluginName, typed } } };

            var results = (await provider.GetSearchResults(searchInfo, CancellationToken.None)).ToList();

            Assert.Equal(ChannelId, Assert.Single(results).ProviderIds[Constants.PluginName]);
            resolver.Verify(r => r.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("https://www.youtube.com/channel/" + ChannelId)]
        [InlineData(ChannelId)]
        public async Task GetSearchResults_FetchesChannelDirectly_WhenIdOrChannelUrlTypedIntoNameBox(string typed)
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());

            var results = (await provider.GetSearchResults(new SeriesInfo { Name = typed }, CancellationToken.None)).ToList();

            Assert.Single(results);
        }

        [Fact]
        public async Task GetSearchResults_ListsIdMatchFirst_ThenNameSearch_WithoutDuplicates()
        {
            var other = new Channel { Id = "UCzzzzzzzzzzzzzzzzzzzzzz", Snippet = new ChannelSnippet { Title = "Rick Astley Fan Club" } };
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);
            resolver.Setup(r => r.SearchChannelsAsync("Rick Astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { other, SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var searchInfo = new SeriesInfo
            {
                Name = "Rick Astley",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var results = (await provider.GetSearchResults(searchInfo, CancellationToken.None)).ToList();

            Assert.Equal(new[] { ChannelId, "UCzzzzzzzzzzzzzzzzzzzzzz" }, results.Select(r => r.ProviderIds[Constants.PluginName]));
        }

        [Fact]
        public async Task GetSearchResults_FallsBackToTypedName_WhenTypedIdDoesNotExist()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetChannelAsync("UCdoesnotexist000000000a", It.IsAny<CancellationToken>())).ReturnsAsync((Channel?)null);
            resolver.Setup(r => r.SearchChannelsAsync("Rick Astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());
            var searchInfo = new SeriesInfo
            {
                Name = "Rick Astley",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, "UCdoesnotexist000000000a" } }
            };

            Assert.Single(await provider.GetSearchResults(searchInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GetMetadata_ResolvesHandleStoredInIdField_EvenWithNameSearchDisabled()
        {
            // Typing a handle into the series' "YouTube" external ID field in the metadata editor
            // is explicit user input: honour it regardless of the automatic folder-name setting,
            // and hand back the real channel ID so it replaces the handle on the item.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("@FaithvilleProductions", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var provider = new YoutubeSeriesProvider(resolver.Object, Config(nameSearch: false));
            var info = new SeriesInfo
            {
                Path = "/media/channels/Some Folder",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, "@FaithvilleProductions" } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal(ChannelId, result.Item.ProviderIds[Constants.PluginName]);
        }

        [Fact]
        public async Task GetSearchResults_UsesConfiguredResultLimit()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchChannelsAsync("Rick Astley", 25, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config(limit: 25));

            var results = await provider.GetSearchResults(new SeriesInfo { Name = "Rick Astley" }, CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task GetSearchResults_UsesFolderName_WhenNoNameGiven()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchChannelsAsync("Rick Astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel });

            var provider = new YoutubeSeriesProvider(resolver.Object, Config());

            var results = await provider.GetSearchResults(new SeriesInfo { Path = "/media/channels/Rick Astley" }, CancellationToken.None);

            Assert.Single(results);
        }

        [Fact]
        public async Task GetSearchResults_ReturnsEmpty_WhenNothingToSearchFor()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            var provider = new YoutubeSeriesProvider(resolver.Object, Config());

            var results = await provider.GetSearchResults(new SeriesInfo(), CancellationToken.None);

            Assert.Empty(results);
            resolver.VerifyNoOtherCalls();
        }
    }
}
