using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests.YouTube
{
    public class YouTubeApiClientTests
    {
        private const string VideoListResponse = @"{
            ""kind"": ""youtube#videoListResponse"",
            ""items"": [
                {
                    ""kind"": ""youtube#video"",
                    ""id"": ""dQw4w9WgXcQ"",
                    ""snippet"": {
                        ""publishedAt"": ""2009-10-25T06:57:33Z"",
                        ""channelId"": ""UCuAXFkgsw1L7xaCfnd5JJOw"",
                        ""title"": ""Rick Astley - Never Gonna Give You Up"",
                        ""description"": ""The official video."",
                        ""channelTitle"": ""Rick Astley""
                    },
                    ""contentDetails"": {
                        ""duration"": ""PT3M33S""
                    },
                    ""statistics"": {
                        ""viewCount"": ""1000000000"",
                        ""likeCount"": ""12000000""
                    },
                    ""topicDetails"": {
                        ""topicCategories"": [""https://en.wikipedia.org/wiki/Pop_music""]
                    }
                }
            ]
        }";

        private const string EmptyListResponse = @"{ ""kind"": ""youtube#videoListResponse"", ""items"": [] }";

        private const string ChannelListResponse = @"{
            ""kind"": ""youtube#channelListResponse"",
            ""items"": [
                {
                    ""kind"": ""youtube#channel"",
                    ""id"": ""UCuAXFkgsw1L7xaCfnd5JJOw"",
                    ""snippet"": {
                        ""title"": ""Rick Astley"",
                        ""description"": ""The official Rick Astley YouTube channel."",
                        ""customUrl"": ""@rickastleyyt"",
                        ""country"": ""GB""
                    },
                    ""statistics"": {
                        ""subscriberCount"": ""3000000""
                    },
                    ""brandingSettings"": {
                        ""channel"": { ""keywords"": ""\""rick astley\"" music 80s"" },
                        ""image"": { ""bannerExternalUrl"": ""https://yt3.googleusercontent.com/banner"" }
                    },
                    ""topicDetails"": {
                        ""topicCategories"": [""https://en.wikipedia.org/wiki/Music""]
                    }
                }
            ]
        }";

        private const string EmptySearchResponse = @"{ ""kind"": ""youtube#searchListResponse"", ""items"": [] }";

        [Fact]
        public void Constructor_DoesNotThrowWithoutApiKey()
        {
            // Jellyfin constructs providers (and this client) once at server startup to register
            // them, before the admin has necessarily configured a key yet. The constructor must
            // stay lenient - otherwise every provider silently fails to register until a restart.
            var exception = Record.Exception(() => new YouTubeApiClient(string.Empty));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetVideoAsync_ThrowsWhenApiKeyMissing(string apiKey)
        {
            using var client = new YouTubeApiClient(apiKey);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None));
        }

        [Fact]
        public async Task GetVideoAsync_ReturnsParsedVideo_RequestingEveryMappedPart()
        {
            var handler = new FakeHttpMessageHandler(VideoListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var video = await client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);

            Assert.NotNull(video);
            Assert.Equal("dQw4w9WgXcQ", video!.Id);
            Assert.Equal("Rick Astley - Never Gonna Give You Up", video.Snippet.Title);
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", video.Snippet.ChannelId);
            Assert.Equal("PT3M33S", video.ContentDetails.Duration);
            Assert.Equal("https://en.wikipedia.org/wiki/Pop_music", Assert.Single(video.TopicDetails.TopicCategories));

            Assert.NotNull(handler.LastRequest);
            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("id=dQw4w9WgXcQ", query);
            Assert.Contains("key=TESTKEY", query);
            Assert.Contains("part=snippet,contentDetails,statistics,topicDetails,status,liveStreamingDetails", query);
        }

        [Fact]
        public async Task ApiKey_IsReadOnEveryCall_SoAKeySavedAfterStartupIsUsed()
        {
            // Jellyfin constructs the client at startup, before the admin has entered a key; the key
            // must be picked up (and a changed key applied) without restarting the server.
            var apiKey = string.Empty;
            var handler = new FakeHttpMessageHandler(VideoListResponse);
            using var client = new YouTubeApiClient(() => apiKey, new FakeGoogleHttpClientFactory(handler));

            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None));

            apiKey = "FIRSTKEY";
            await client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);
            Assert.Contains("key=FIRSTKEY", handler.LastRequest!.RequestUri!.Query);

            apiKey = " SECONDKEY ";
            await client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);
            Assert.Contains("key=SECONDKEY", handler.LastRequest!.RequestUri!.Query);
        }

        [Fact]
        public async Task GetVideoAsync_ReturnsNullWhenNotFound()
        {
            var handler = new FakeHttpMessageHandler(EmptyListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var video = await client.GetVideoAsync("doesnotexist", CancellationToken.None);

            Assert.Null(video);
        }

        [Fact]
        public async Task GetVideosAsync_SendsAllIdsInOneRequest()
        {
            var handler = new FakeHttpMessageHandler(VideoListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var videos = await client.GetVideosAsync(new[] { "dQw4w9WgXcQ", "aaaaaaaaaaa", "dQw4w9WgXcQ" }, CancellationToken.None);

            Assert.Single(videos);
            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("id=dQw4w9WgXcQ,aaaaaaaaaaa", query);
        }

        [Fact]
        public async Task GetVideosAsync_SplitsMoreThanFiftyIdsAcrossRequests()
        {
            var requests = new List<HttpRequestMessage>();
            var handler = new FakeHttpMessageHandler(request =>
            {
                requests.Add(request);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmptyListResponse, Encoding.UTF8, "application/json")
                };
            });
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var ids = Enumerable.Range(0, 120).Select(i => $"vid{i:D8}").ToList();
            await client.GetVideosAsync(ids, CancellationToken.None);

            Assert.Equal(3, requests.Count);
            Assert.Equal(50, CountIds(requests[0]));
            Assert.Equal(50, CountIds(requests[1]));
            Assert.Equal(20, CountIds(requests[2]));
        }

        [Fact]
        public async Task GetVideosAsync_MakesNoRequest_ForNoIds()
        {
            var handler = new FakeHttpMessageHandler(EmptyListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var videos = await client.GetVideosAsync(Array.Empty<string>(), CancellationToken.None);

            Assert.Empty(videos);
            Assert.Null(handler.LastRequest);
        }

        [Fact]
        public async Task GetChannelAsync_ReturnsParsedChannel_RequestingEveryMappedPart()
        {
            var handler = new FakeHttpMessageHandler(ChannelListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var channel = await client.GetChannelAsync("UCuAXFkgsw1L7xaCfnd5JJOw", CancellationToken.None);

            Assert.NotNull(channel);
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", channel!.Id);
            Assert.Equal("Rick Astley", channel.Snippet.Title);
            Assert.Equal("@rickastleyyt", channel.Snippet.CustomUrl);
            Assert.Equal("\"rick astley\" music 80s", channel.BrandingSettings.Channel.Keywords);
            Assert.Equal("https://yt3.googleusercontent.com/banner", channel.BrandingSettings.Image.BannerExternalUrl);

            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("id=UCuAXFkgsw1L7xaCfnd5JJOw", query);
            Assert.Contains("part=snippet,brandingSettings,statistics,topicDetails,contentDetails,status", query);
        }

        [Fact]
        public async Task GetChannelsAsync_SendsAllIdsInOneRequest()
        {
            var handler = new FakeHttpMessageHandler(ChannelListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var channels = await client.GetChannelsAsync(new[] { "UCuAXFkgsw1L7xaCfnd5JJOw", "UCzzzzzzzzzzzzzzzzzzzzzz" }, CancellationToken.None);

            Assert.Single(channels);
            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("id=UCuAXFkgsw1L7xaCfnd5JJOw,UCzzzzzzzzzzzzzzzzzzzzzz", query);
        }

        [Theory]
        [InlineData("@rickastleyyt")]
        [InlineData("rickastleyyt")]
        [InlineData("  @rickastleyyt  ")]
        public async Task GetChannelByHandleAsync_UsesForHandle_WithLeadingAt(string handle)
        {
            var handler = new FakeHttpMessageHandler(ChannelListResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            var channel = await client.GetChannelByHandleAsync(handle, CancellationToken.None);

            Assert.NotNull(channel);
            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("forHandle=@rickastleyyt", query);
            Assert.DoesNotContain("id=", query);
        }

        [Fact]
        public async Task GetChannelByHandleAsync_ReturnsNull_ForUnknownHandle()
        {
            var handler = new FakeHttpMessageHandler(@"{ ""kind"": ""youtube#channelListResponse"", ""items"": [] }");
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            Assert.Null(await client.GetChannelByHandleAsync("@nobody", CancellationToken.None));
        }

        [Fact]
        public async Task SearchVideosAsync_SetsVideoTypeAndQuery()
        {
            var handler = new FakeHttpMessageHandler(EmptySearchResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            // 10 is deliberately different from the API's default maxResults (5): the Google client
            // omits query parameters that equal their default value, so testing with 5 would pass
            // even if MaxResults were never wired up.
            var results = await client.SearchVideosAsync("3blue1brown", null, 10, CancellationToken.None);

            Assert.Empty(results);
            var query = handler.LastRequest!.RequestUri!.Query;
            Assert.Contains("q=3blue1brown", query);
            Assert.Contains("type=video", query);
            Assert.Contains("maxResults=10", query);
            Assert.DoesNotContain("channelId=", query);
        }

        [Fact]
        public async Task SearchVideosAsync_ScopesToChannel_WhenGiven()
        {
            var handler = new FakeHttpMessageHandler(EmptySearchResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            await client.SearchVideosAsync("never gonna", "UCuAXFkgsw1L7xaCfnd5JJOw", 10, CancellationToken.None);

            var query = handler.LastRequest!.RequestUri!.Query;
            Assert.Contains("channelId=UCuAXFkgsw1L7xaCfnd5JJOw", query);
            Assert.Contains("type=video", query);
        }

        [Fact]
        public async Task SearchChannelsAsync_SetsChannelTypeAndQuery()
        {
            var handler = new FakeHttpMessageHandler(EmptySearchResponse);
            using var client = new YouTubeApiClient("TESTKEY", new FakeGoogleHttpClientFactory(handler));

            await client.SearchChannelsAsync("rick astley", 10, CancellationToken.None);

            var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
            Assert.Contains("q=rick astley", query);
            Assert.Contains("type=channel", query);
            Assert.Contains("maxResults=10", query);
        }

        private static int CountIds(HttpRequestMessage request)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            var idParam = query.TrimStart('?').Split('&').Single(p => p.StartsWith("id=", StringComparison.Ordinal));
            return idParam["id=".Length..].Split(',').Length;
        }
    }
}
