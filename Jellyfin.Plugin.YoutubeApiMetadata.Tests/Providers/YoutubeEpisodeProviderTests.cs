using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.Providers;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Moq;
using Xunit;
using InternalItemsQuery = MediaBrowser.Controller.Entities.InternalItemsQuery;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.Providers
{
    public class YoutubeEpisodeProviderTests
    {
        private const string ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw";

        private static readonly Video SampleVideo = new()
        {
            Id = "dQw4w9WgXcQ",
            Snippet = new VideoSnippet
            {
                Title = "Never Gonna Give You Up",
                Description = "The official video.",
                ChannelId = ChannelId,
                PublishedAtDateTimeOffset = new DateTimeOffset(2009, 10, 25, 6, 57, 33, TimeSpan.Zero)
            }
        };

        private static Func<PluginConfiguration> Config(bool titleFallback = true, bool nameSearch = true, int limit = 10)
            => () => new PluginConfiguration
            {
                EnableTitleSearchFallback = titleFallback,
                EnableChannelNameSearch = nameSearch,
                SearchResultLimit = limit
            };

        /// <summary>
        /// A loose <see cref="ILibraryManager"/> mock with no channel/sibling matches configured;
        /// suitable for tests that don't exercise the episode-numbering path.
        /// </summary>
        private static Mock<ILibraryManager> EmptyLibraryManager()
        {
            var mock = new Mock<ILibraryManager>();
            mock.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(Array.Empty<MediaBrowser.Controller.Entities.BaseItem>());
            return mock;
        }

        private static YoutubeEpisodeProvider CreateProvider(Mock<IYoutubeMetadataResolver> resolver, Func<PluginConfiguration>? config = null)
            => new(resolver.Object, EmptyLibraryManager().Object, config ?? Config());

        [Fact]
        public async Task GetMetadata_ResolvesVideoIdFromFileName()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = "Some Video [dQw4w9WgXcQ].mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Never Gonna Give You Up", result.Item.Name);
        }

        [Fact]
        public async Task GetMetadata_PrefersStoredProviderIdOverFileName()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo
            {
                Path = "/media/Rick Astley/Renamed after identify [aaaaaaaaaaa].mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, "dQw4w9WgXcQ" } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            resolver.Verify(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetMetadata_FallsBackToTitleSearchInChannel_WhenFileNameHasNoId()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver
                .Setup(r => r.FindVideoByTitleAsync("Never Gonna Give You Up", ChannelId, new DateTime(2009, 10, 25, 0, 0, 0, DateTimeKind.Utc), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo
            {
                Path = "/media/Rick Astley/20091025 - Never Gonna Give You Up.mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("dQw4w9WgXcQ", result.Item.ProviderIds[Constants.PluginName]);
        }

        [Fact]
        public async Task GetMetadata_ResolvesChannelFromParentFolderName_ForTitleSearch()
        {
            // No series provider ID passed in (first scan of a brand-new folder), so the channel is
            // looked up from the parent folder's name before the video title search is scoped to it.
            var channel = new Channel { Id = ChannelId, Snippet = new ChannelSnippet { Title = "Rick Astley" } };
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("Rick Astley", It.IsAny<CancellationToken>())).ReturnsAsync(channel);
            resolver
                .Setup(r => r.FindVideoByTitleAsync("Never Gonna Give You Up", ChannelId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = "/media/Rick Astley/Never Gonna Give You Up.mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_UsesChannelIdFromParentFolder_WithoutSearching()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver
                .Setup(r => r.FindVideoByTitleAsync("Never Gonna Give You Up", ChannelId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = $"/media/Rick Astley [{ChannelId}]/Never Gonna Give You Up.mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            resolver.Verify(r => r.FindChannelByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetMetadata_DoesNotTitleSearchSiteWide_WhenChannelUnknown()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.FindChannelByNameAsync("Unknown Folder", It.IsAny<CancellationToken>())).ReturnsAsync((Channel?)null);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = "/media/Unknown Folder/Some Title.mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
            resolver.Verify(r => r.FindVideoByTitleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetMetadata_ReturnsEmptyResult_WhenPathHasNoId_AndFallbackDisabled()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);

            var provider = CreateProvider(resolver, Config(titleFallback: false));
            var info = new EpisodeInfo { Path = "no id here.mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
            resolver.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetMetadata_DoesNotMistakeParentFolderChannelId_ForVideoId()
        {
            // Regression: the original GetYTID(path) matched the 24-char "[channelId]" of the parent
            // folder when the file name had no "[videoId]", and asked the API for a video by that ID.
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver
                .Setup(r => r.FindVideoByTitleAsync("Some Title", ChannelId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Video?)null);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = $"C:\\media\\Rick Astley [{ChannelId}]\\Some Title.mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
            resolver.Verify(r => r.GetVideoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetMetadata_ReturnsEmptyResult_WhenVideoNotFound()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>();
            resolver.Setup(r => r.GetVideoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Video?)null);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo { Path = "Deleted Video [aaaaaaaaaaa].mkv" };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task GetMetadata_SetsEpisodeOne_WhenChannelHasNoOtherKnownVideos()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>();
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo
            {
                Path = "Some Video [dQw4w9WgXcQ].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.Equal(1, result.Item.ParentIndexNumber);
            Assert.Equal(1, result.Item.IndexNumber);
        }

        [Fact]
        public async Task GetMetadata_RanksAmongSiblingEpisodes_ByPremiereDate()
        {
            var video = new Video
            {
                Id = "newVideo111",
                Snippet = new VideoSnippet
                {
                    Title = "Third video",
                    ChannelId = ChannelId,
                    PublishedAtDateTimeOffset = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero)
                }
            };

            var resolver = new Mock<IYoutubeMetadataResolver>();
            resolver.Setup(r => r.GetVideoAsync("newVideo111", It.IsAny<CancellationToken>())).ReturnsAsync(video);

            var series = new Series { Id = Guid.NewGuid() };
            series.ProviderIds.Add(Constants.PluginName, ChannelId);

            var earlier = new Episode { PremiereDate = new DateTime(2020, 1, 1) };
            earlier.ProviderIds.Add(Constants.PluginName, "earlierVid1");

            var later = new Episode { PremiereDate = new DateTime(2025, 12, 1) };
            later.ProviderIds.Add(Constants.PluginName, "laterVid111");

            var libraryManager = new Mock<ILibraryManager>();
            libraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Length == 1 && q.IncludeItemTypes[0] == BaseItemKind.Series)))
                .Returns(new MediaBrowser.Controller.Entities.BaseItem[] { series });
            libraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.IncludeItemTypes.Length == 1 && q.IncludeItemTypes[0] == BaseItemKind.Episode)))
                .Returns(new MediaBrowser.Controller.Entities.BaseItem[] { earlier, later });

            var provider = new YoutubeEpisodeProvider(resolver.Object, libraryManager.Object, Config());
            var info = new EpisodeInfo
            {
                Path = "Some Video [newVideo111].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.Equal(1, result.Item.ParentIndexNumber);
            Assert.Equal(2, result.Item.IndexNumber);
        }

        [Fact]
        public async Task GetMetadata_RanksSiblings_UsingVideosOwnChannel_WhenSeriesIdMissing()
        {
            // Nothing tells us the series' channel up front, but the fetched video says which
            // channel it belongs to - that's enough to find the siblings.
            var resolver = new Mock<IYoutubeMetadataResolver>();
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var series = new Series { Id = Guid.NewGuid() };
            series.ProviderIds.Add(Constants.PluginName, ChannelId);
            var earlier = new Episode { PremiereDate = new DateTime(2000, 1, 1) };
            earlier.ProviderIds.Add(Constants.PluginName, "earlierVid1");

            var libraryManager = new Mock<ILibraryManager>();
            libraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.HasAnyProviderId != null && q.HasAnyProviderId[Constants.PluginName] == ChannelId)))
                .Returns(new MediaBrowser.Controller.Entities.BaseItem[] { series });
            libraryManager
                .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.AncestorIds.Length == 1)))
                .Returns(new MediaBrowser.Controller.Entities.BaseItem[] { earlier });

            var provider = new YoutubeEpisodeProvider(resolver.Object, libraryManager.Object, Config());

            var result = await provider.GetMetadata(new EpisodeInfo { Path = "Some Video [dQw4w9WgXcQ].mkv" }, CancellationToken.None);

            Assert.Equal(2, result.Item.IndexNumber);
        }

        [Fact]
        public async Task GetSearchResults_UsesStoredProviderIdWhenAvailable()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo
            {
                Path = "Some Other File Name.mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.PluginName, "dQw4w9WgXcQ" } }
            };

            var results = (await provider.GetSearchResults(info, CancellationToken.None)).ToList();

            var single = Assert.Single(results);
            Assert.Equal(2009, single.ProductionYear);
            Assert.Equal("The official video.", single.Overview);
        }

        [Fact]
        public async Task GetSearchResults_SearchesWithinChannel_WhenNoIdAvailable()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchVideosAsync("never gonna", ChannelId, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Video> { SampleVideo });

            var provider = CreateProvider(resolver);
            var info = new EpisodeInfo
            {
                Name = "never gonna",
                Path = "/media/Rick Astley/whatever.mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.PluginName, ChannelId } }
            };

            var results = (await provider.GetSearchResults(info, CancellationToken.None)).ToList();

            var single = Assert.Single(results);
            Assert.Equal("dQw4w9WgXcQ", single.ProviderIds[Constants.PluginName]);
        }

        [Fact]
        public async Task GetSearchResults_SearchesSiteWide_WhenChannelUnknown_AndNameSearchDisabled()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.SearchVideosAsync("Never Gonna Give You Up", null, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Video> { SampleVideo });

            var provider = CreateProvider(resolver, Config(nameSearch: false));
            var info = new EpisodeInfo { Path = "/media/Somewhere/20091025 - Never Gonna Give You Up.mkv" };

            var results = (await provider.GetSearchResults(info, CancellationToken.None)).ToList();

            Assert.Single(results);
        }

        [Fact]
        public async Task GetSearchResults_ReturnsEmpty_WhenNothingToSearchFor()
        {
            var resolver = new Mock<IYoutubeMetadataResolver>(MockBehavior.Strict);
            var provider = CreateProvider(resolver);

            var results = await provider.GetSearchResults(new EpisodeInfo(), CancellationToken.None);

            Assert.Empty(results);
            resolver.VerifyNoOtherCalls();
        }
    }
}
