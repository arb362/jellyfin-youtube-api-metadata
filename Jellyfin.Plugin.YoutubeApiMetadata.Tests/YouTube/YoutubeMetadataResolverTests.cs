using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.Caching;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.YouTube
{
    public class YoutubeMetadataResolverTests
    {
        private const string ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw";
        private const string OtherChannelId = "UCzzzzzzzzzzzzzzzzzzzzzz";

        private static readonly Video SampleVideo = new() { Id = "dQw4w9WgXcQ", Snippet = new VideoSnippet { Title = "Never Gonna Give You Up" } };
        private static readonly Channel SampleChannel = new() { Id = ChannelId, Snippet = new ChannelSnippet { Title = "Rick Astley", CustomUrl = "@rickastley" } };
        private static readonly Channel OtherChannel = new() { Id = OtherChannelId, Snippet = new ChannelSnippet { Title = "Rick Astley Fan Club" } };

        private static SearchResult ChannelHit(string channelId, string title)
            => new() { Id = new ResourceId { ChannelId = channelId }, Snippet = new SearchResultSnippet { Title = title } };

        private static SearchResult VideoHit(string videoId, string title)
            => new() { Id = new ResourceId { VideoId = videoId }, Snippet = new SearchResultSnippet { Title = title } };

        /// <summary>
        /// A loose cache mock that misses on everything and accepts every save.
        /// </summary>
        private static Mock<IMetadataCache> EmptyCache()
        {
            var cache = new Mock<IMetadataCache>();
            cache.Setup(c => c.GetVideoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Video?)null);
            cache.Setup(c => c.GetChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Channel?)null);
            cache.Setup(c => c.GetChannelIdForNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            return cache;
        }

        [Fact]
        public async Task GetVideoAsync_UsesCacheWhenFresh_AndDoesNotCallApi()
        {
            var cache = new Mock<IMetadataCache>();
            cache.Setup(c => c.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);

            Assert.Same(SampleVideo, result);
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetVideoAsync_FallsBackToApiAndSavesToCache_WhenNotCached()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>();
            client.Setup(c => c.GetVideoAsync("dQw4w9WgXcQ", It.IsAny<CancellationToken>())).ReturnsAsync(SampleVideo);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);

            Assert.Same(SampleVideo, result);
            cache.Verify(c => c.SaveVideoAsync("dQw4w9WgXcQ", SampleVideo, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetVideoAsync_DoesNotCacheWhenApiReturnsNothing()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>();
            client.Setup(c => c.GetVideoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Video?)null);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.GetVideoAsync("deleted", CancellationToken.None);

            Assert.Null(result);
            cache.Verify(c => c.SaveVideoAsync(It.IsAny<string>(), It.IsAny<Video>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetChannelAsync_UsesCacheWhenFresh_AndDoesNotCallApi()
        {
            var cache = new Mock<IMetadataCache>();
            cache.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.GetChannelAsync(ChannelId, CancellationToken.None);

            Assert.Same(SampleChannel, result);
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetChannelAsync_FallsBackToApiAndSavesToCache_WhenNotCached()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>();
            client.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.GetChannelAsync(ChannelId, CancellationToken.None);

            Assert.Same(SampleChannel, result);
            cache.Verify(c => c.SaveChannelAsync(ChannelId, SampleChannel, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FindChannelByNameAsync_UsesCachedNameLookup_WithoutSearching()
        {
            var cache = EmptyCache();
            cache.Setup(c => c.GetChannelIdForNameAsync("rick astley", It.IsAny<CancellationToken>())).ReturnsAsync(ChannelId);
            cache.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("Rick Astley", CancellationToken.None);

            Assert.Same(SampleChannel, result);
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task FindChannelByNameAsync_SearchesThenFetchesFullChannel_AndCachesNameLookup()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchChannelsAsync("Rick Astley", 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { ChannelHit(ChannelId, "Rick Astley") });
            client.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("Rick Astley", CancellationToken.None);

            Assert.Same(SampleChannel, result);
            cache.Verify(c => c.SaveChannelIdForNameAsync("rick astley", ChannelId, It.IsAny<CancellationToken>()), Times.Once);
            cache.Verify(c => c.SaveChannelAsync(ChannelId, SampleChannel, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task FindChannelByNameAsync_PrefersExactTitleMatch_OverTopHit()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchChannelsAsync("Rick Astley", 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult>
                {
                    ChannelHit(OtherChannelId, "Rick Astley Fan Club"),
                    ChannelHit(ChannelId, "Rick Astley")
                });
            client.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("Rick Astley", CancellationToken.None);

            Assert.Equal(ChannelId, result!.Id);
            client.Verify(c => c.GetChannelAsync(OtherChannelId, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FindChannelByNameAsync_ResolvesHandleDirectly_WithoutSearching()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetChannelByHandleAsync("@rickastley", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("@rickastley", CancellationToken.None);

            Assert.Same(SampleChannel, result);
            client.Verify(c => c.SearchChannelsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            cache.Verify(c => c.SaveChannelIdForNameAsync("rickastley", ChannelId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FindChannelByNameAsync_FallsBackToSearch_WhenHandleUnknown()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetChannelByHandleAsync("@rickastley", It.IsAny<CancellationToken>())).ReturnsAsync((Channel?)null);
            client.Setup(c => c.SearchChannelsAsync("@rickastley", 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { ChannelHit(ChannelId, "Rick Astley") });
            client.Setup(c => c.GetChannelAsync(ChannelId, It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("@rickastley", CancellationToken.None);

            Assert.Same(SampleChannel, result);
        }

        [Fact]
        public async Task FindChannelByNameAsync_ReturnsNull_AndCachesNothing_WhenNoHits()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>();
            client.Setup(c => c.SearchChannelsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult>());

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var result = await resolver.FindChannelByNameAsync("Nobody", CancellationToken.None);

            Assert.Null(result);
            cache.Verify(c => c.SaveChannelIdForNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("@")]
        public async Task FindChannelByNameAsync_ReturnsNull_ForBlankName(string name)
        {
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);

            Assert.Null(await resolver.FindChannelByNameAsync(name, CancellationToken.None));
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SearchChannelsAsync_HydratesHitsInOneBatch_PreservingRelevanceOrder()
        {
            var cache = EmptyCache();
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchChannelsAsync("astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { ChannelHit(OtherChannelId, "Rick Astley Fan Club"), ChannelHit(ChannelId, "Rick Astley") });
            client.Setup(c => c.GetChannelsAsync(
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { OtherChannelId, ChannelId })),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { SampleChannel, OtherChannel }); // API returns in arbitrary order

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var results = await resolver.SearchChannelsAsync("astley", 10, CancellationToken.None);

            Assert.Equal(new[] { OtherChannelId, ChannelId }, results.Select(c => c.Id));
            cache.Verify(c => c.SaveChannelAsync(It.IsAny<string>(), It.IsAny<Channel>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SearchChannelsAsync_PutsExactTitleMatchFirst()
        {
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchChannelsAsync("rick astley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { ChannelHit(OtherChannelId, "Rick Astley Fan Club"), ChannelHit(ChannelId, "Rick Astley") });
            client.Setup(c => c.GetChannelsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { OtherChannel, SampleChannel });

            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);
            var results = await resolver.SearchChannelsAsync("rick astley", 10, CancellationToken.None);

            Assert.Equal(new[] { ChannelId, OtherChannelId }, results.Select(c => c.Id));
        }

        [Fact]
        public async Task SearchChannelsAsync_ResolvesHandleFirst_ThenSearches_WithoutDuplicating()
        {
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetChannelByHandleAsync("@rickastley", It.IsAny<CancellationToken>())).ReturnsAsync(SampleChannel);
            client.Setup(c => c.SearchChannelsAsync("@rickastley", 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { ChannelHit(ChannelId, "Rick Astley"), ChannelHit(OtherChannelId, "Rick Astley Fan Club") });
            client.Setup(c => c.GetChannelsAsync(
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { OtherChannelId })),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Channel> { OtherChannel });

            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);
            var results = await resolver.SearchChannelsAsync("@rickastley", 10, CancellationToken.None);

            Assert.Equal(new[] { ChannelId, OtherChannelId }, results.Select(c => c.Id));
        }

        [Fact]
        public async Task SearchChannelsAsync_ReturnsEmpty_ForBlankQuery()
        {
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);

            Assert.Empty(await resolver.SearchChannelsAsync("  ", 10, CancellationToken.None));
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SearchVideosAsync_HydratesHits_ScopedToChannel_AndCachesEach()
        {
            var cache = EmptyCache();
            var other = new Video { Id = "otherVid111", Snippet = new VideoSnippet { Title = "Never Gonna Give You Up (Live)" } };
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchVideosAsync("never gonna", ChannelId, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { VideoHit("otherVid111", "Live"), VideoHit("dQw4w9WgXcQ", "Official") });
            client.Setup(c => c.GetVideosAsync(
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "otherVid111", "dQw4w9WgXcQ" })),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Video> { SampleVideo, other });

            var resolver = new YoutubeMetadataResolver(client.Object, cache.Object);
            var results = await resolver.SearchVideosAsync("never gonna", ChannelId, 10, CancellationToken.None);

            Assert.Equal(new[] { "otherVid111", "dQw4w9WgXcQ" }, results.Select(v => v.Id));
            cache.Verify(c => c.SaveVideoAsync("dQw4w9WgXcQ", SampleVideo, It.IsAny<CancellationToken>()), Times.Once);
            cache.Verify(c => c.SaveVideoAsync("otherVid111", other, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SearchVideosAsync_ReturnsEmpty_WhenNoHits_WithoutFetching()
        {
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchVideosAsync("nothing", null, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult>());

            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);

            Assert.Empty(await resolver.SearchVideosAsync("nothing", null, 10, CancellationToken.None));
            client.Verify(c => c.GetVideosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FindVideoByTitleAsync_ReturnsExactMatch_FromChannelScopedSearch()
        {
            var live = new Video { Id = "otherVid111", Snippet = new VideoSnippet { Title = "Never Gonna Give You Up (Live)" } };
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchVideosAsync("Never Gonna Give You Up", ChannelId, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { VideoHit("otherVid111", "Live"), VideoHit("dQw4w9WgXcQ", "Official") });
            client.Setup(c => c.GetVideosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Video> { live, SampleVideo });

            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);
            var result = await resolver.FindVideoByTitleAsync("Never Gonna Give You Up", ChannelId, null, CancellationToken.None);

            Assert.Equal("dQw4w9WgXcQ", result!.Id);
        }

        [Fact]
        public async Task FindVideoByTitleAsync_ReturnsNull_WhenNothingMatchesConfidently()
        {
            var live = new Video { Id = "otherVid111", Snippet = new VideoSnippet { Title = "Never Gonna Give You Up (Live)" } };
            var client = new Mock<IYouTubeApiClient>(MockBehavior.Strict);
            client.Setup(c => c.SearchVideosAsync("Never Gonna Give You Up", ChannelId, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SearchResult> { VideoHit("otherVid111", "Live") });
            client.Setup(c => c.GetVideosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Video> { live });

            var resolver = new YoutubeMetadataResolver(client.Object, EmptyCache().Object);
            var result = await resolver.FindVideoByTitleAsync("Never Gonna Give You Up", ChannelId, null, CancellationToken.None);

            Assert.Null(result);
        }
    }
}
