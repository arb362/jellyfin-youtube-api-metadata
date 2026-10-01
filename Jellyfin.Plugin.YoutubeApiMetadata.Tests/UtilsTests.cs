using System;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.YouTube.v3.Data;
using Xunit;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Tests
{
    public class UtilsTests
    {
        [Theory]
        [InlineData("3Blue1Brown - 20190113 - The_most_unexpected_answer_to_a_counting_puzzle [HEfHFsfGXjs].mkv", "HEfHFsfGXjs")]
        [InlineData("Foo", "")]
        [InlineData("3Blue1Brown - NA - 3Blue1Brown_-_Videos [UCYO_jab_esuFRV4b17AJtAw].info.json", "UCYO_jab_esuFRV4b17AJtAw")]
        public void GetYTIDTest(string fileName, string expected)
        {
            Assert.Equal(expected, Utils.GetYTID(fileName));
        }

        [Theory]
        [InlineData("Some Video [dQw4w9WgXcQ].mkv", "dQw4w9WgXcQ", "")]
        [InlineData("Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]", "", "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("/media/Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]/Some Video [dQw4w9WgXcQ].mkv", "dQw4w9WgXcQ", "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("nothing here", "", "")]
        public void GetVideoId_And_GetChannelId_OnlyMatchTheirOwnKind(string text, string videoId, string channelId)
        {
            Assert.Equal(videoId, Utils.GetVideoId(text));
            Assert.Equal(channelId, Utils.GetChannelId(text));
        }

        [Theory]
        [InlineData("/media/channels/Rick Astley/video.mkv", "video.mkv")]
        [InlineData("C:\\media\\channels\\Rick Astley\\video.mkv", "video.mkv")]
        [InlineData("/media/channels/Rick Astley/", "Rick Astley")]
        [InlineData("video.mkv", "video.mkv")]
        public void GetLastPathSegment_IsSeparatorAgnostic(string path, string expected)
        {
            Assert.Equal(expected, Utils.GetLastPathSegment(path));
        }

        [Theory]
        [InlineData("/media/channels/Rick Astley/video.mkv", "/media/channels/Rick Astley")]
        [InlineData("C:\\media\\Rick Astley\\video.mkv", "C:\\media\\Rick Astley")]
        [InlineData("video.mkv", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void GetParentPath_IsSeparatorAgnostic(string? path, string? expected)
        {
            Assert.Equal(expected, Utils.GetParentPath(path));
        }

        [Fact]
        public void ResolveChannelId_IgnoresVideoIdInPath_AndIdsInAncestorFolders()
        {
            Assert.Null(Utils.ResolveChannelId(null, "/media/Some Video [dQw4w9WgXcQ]", null));
            Assert.Null(Utils.ResolveChannelId(null, "/media/Parent [UCuAXFkgsw1L7xaCfnd5JJOw]/Child", null));
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", Utils.ResolveChannelId(null, "/media/Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]", null));
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", Utils.ResolveChannelId(null, "/media/Rick Astley", "Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]"));
        }

        [Theory]
        [InlineData("UCuAXFkgsw1L7xaCfnd5JJOw", ChannelReferenceKind.Id, "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("  UCuAXFkgsw1L7xaCfnd5JJOw ", ChannelReferenceKind.Id, "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("@FaithvilleProductions", ChannelReferenceKind.Handle, "@FaithvilleProductions")]
        [InlineData("Faithville Productions", ChannelReferenceKind.Name, "Faithville Productions")]
        [InlineData("FaithvilleProductions", ChannelReferenceKind.Name, "FaithvilleProductions")]
        [InlineData("ABCDEFGHIJKLMNOPQRSTUVWX", ChannelReferenceKind.Name, "ABCDEFGHIJKLMNOPQRSTUVWX")]
        [InlineData("https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw", ChannelReferenceKind.Id, "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw/videos?view=0", ChannelReferenceKind.Id, "UCuAXFkgsw1L7xaCfnd5JJOw")]
        [InlineData("https://www.youtube.com/@FaithvilleProductions", ChannelReferenceKind.Handle, "@FaithvilleProductions")]
        [InlineData("youtube.com/@FaithvilleProductions/videos", ChannelReferenceKind.Handle, "@FaithvilleProductions")]
        [InlineData("https://m.youtube.com/@FaithvilleProductions?si=abc", ChannelReferenceKind.Handle, "@FaithvilleProductions")]
        [InlineData("https://www.youtube.com/c/RickAstley", ChannelReferenceKind.Name, "RickAstley")]
        [InlineData("https://www.youtube.com/user/RickAstleyVEVO", ChannelReferenceKind.Name, "RickAstleyVEVO")]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", ChannelReferenceKind.None, "")]
        [InlineData("https://www.youtube.com/channel/not-an-id", ChannelReferenceKind.None, "")]
        [InlineData("https://www.youtube.com/", ChannelReferenceKind.None, "")]
        [InlineData("", ChannelReferenceKind.None, "")]
        [InlineData("   ", ChannelReferenceKind.None, "")]
        [InlineData(null, ChannelReferenceKind.None, "")]
        public void ParseChannelReference(string? text, ChannelReferenceKind kind, string value)
        {
            var reference = Utils.ParseChannelReference(text);

            Assert.Equal(kind, reference.Kind);
            Assert.Equal(value, reference.Value);
        }

        [Theory]
        [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData(" dQw4w9WgXcQ ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/watch?list=PL123&v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
        [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
        [InlineData("youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
        [InlineData("https://www.youtube.com/@FaithvilleProductions", null)]
        [InlineData("https://example.com/watch?v=dQw4w9WgXcQ", null)]
        [InlineData("@FaithvilleProductions", null)]
        [InlineData("Never Gonna Give You Up", null)]
        [InlineData("UCuAXFkgsw1L7xaCfnd5JJOw", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ParseVideoReference(string? text, string? expected)
        {
            Assert.Equal(expected, Utils.ParseVideoReference(text));
        }

        [Fact]
        public void ResolveChannelId_IgnoresStoredValueThatIsNotAChannelId()
        {
            // A handle typed into the "YouTube" ID field must never be sent to the API as an ID.
            var handle = new Dictionary<string, string> { { Constants.PluginName, "@FaithvilleProductions" } };
            var url = new Dictionary<string, string> { { Constants.PluginName, "https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw" } };

            Assert.Null(Utils.ResolveChannelId(handle, "/media/Faithville", null));
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", Utils.ResolveChannelId(url, "/media/Faithville", null));
        }

        [Theory]
        [InlineData("@rickastley", true)]
        [InlineData("@Rick.Astley_YT-1", true)]
        [InlineData("  @rickastley  ", true)]
        [InlineData("rickastley", false)]
        [InlineData("@ab", false)]
        [InlineData("@rick astley", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsHandle(string? text, bool expected)
        {
            Assert.Equal(expected, Utils.IsHandle(text));
        }

        [Theory]
        [InlineData("Rick Astley", "rick astley")]
        [InlineData("  RICK   ASTLEY  ", "rick astley")]
        [InlineData("Rick-Astley_(Official)!", "rick astley official")]
        [InlineData("@RickAstley", "rickastley")]
        [InlineData("Ünïcødé Näme", "ünïcødé näme")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void NormalizeName(string? name, string expected)
        {
            Assert.Equal(expected, Utils.NormalizeName(name));
        }

        [Theory]
        [InlineData("/media/channels/Rick Astley [UCuAXFkgsw1L7xaCfnd5JJOw]", "Rick Astley")]
        [InlineData("/media/channels/Rick Astley/", "Rick Astley")]
        [InlineData("C:\\media\\channels\\Rick Astley", "Rick Astley")]
        [InlineData("/media/channels/@rickastley", "@rickastley")]
        [InlineData("/media/channels/[UCuAXFkgsw1L7xaCfnd5JJOw]", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void GetChannelNameFromPath(string? path, string? expected)
        {
            Assert.Equal(expected, Utils.GetChannelNameFromPath(path));
        }

        [Fact]
        public void ParseEpisodeFileName_StripsDatePrefixIdAndExtension()
        {
            var (title, date) = Utils.ParseEpisodeFileName("/media/Rick Astley/20091025 - Never Gonna Give You Up [dQw4w9WgXcQ].mkv");

            Assert.Equal("Never Gonna Give You Up", title);
            Assert.Equal(new DateTime(2009, 10, 25, 0, 0, 0, DateTimeKind.Utc), date);
        }

        [Fact]
        public void ParseEpisodeFileName_AcceptsDashedDates_AndChannelPrefix()
        {
            var (title, date) = Utils.ParseEpisodeFileName("Rick Astley - 2009-10-25 - Never Gonna Give You Up.webm");

            Assert.Equal("Never Gonna Give You Up", title);
            Assert.Equal(new DateTime(2009, 10, 25), date!.Value.Date);
        }

        [Fact]
        public void ParseEpisodeFileName_LeavesTitleAlone_WhenNoDatePrefix()
        {
            var (title, date) = Utils.ParseEpisodeFileName("Never Gonna Give You Up.mp4");

            Assert.Equal("Never Gonna Give You Up", title);
            Assert.Null(date);
        }

        [Fact]
        public void ParseEpisodeFileName_DoesNotTreatImpossibleDateAsDate()
        {
            var (title, date) = Utils.ParseEpisodeFileName("20091399 - Not A Date.mp4");

            Assert.Equal("20091399 - Not A Date", title);
            Assert.Null(date);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void ParseEpisodeFileName_HandlesBlankPath(string? path)
        {
            var (title, date) = Utils.ParseEpisodeFileName(path);

            Assert.Equal(string.Empty, title);
            Assert.Null(date);
        }

        [Fact]
        public void PickBestChannelMatch_PrefersExactNormalizedTitle()
        {
            var candidates = new[]
            {
                Hit("UC1", "Rick Astley Fan Club"),
                Hit("UC2", "RICK ASTLEY"),
                Hit("UC3", "Rick Astley")
            };

            Assert.Equal("UC2", Utils.PickBestChannelMatch("rick-astley", candidates)!.Id.ChannelId);
        }

        [Fact]
        public void PickBestChannelMatch_FallsBackToTopHit_SkippingHitsWithoutId()
        {
            var candidates = new[]
            {
                new SearchResult { Id = new ResourceId { VideoId = "notachannel" }, Snippet = new SearchResultSnippet { Title = "Video" } },
                Hit("UC1", "Something Else")
            };

            Assert.Equal("UC1", Utils.PickBestChannelMatch("Rick Astley", candidates)!.Id.ChannelId);
            Assert.Null(Utils.PickBestChannelMatch("Rick Astley", Array.Empty<SearchResult>()));
        }

        [Fact]
        public void OrderChannelsByMatch_MovesExactTitleOrHandleMatchesFirst_KeepingOrderOtherwise()
        {
            var channels = new[]
            {
                Ch("UC1", "Rick Astley Fan Club"),
                Ch("UC2", "Rick Astley Covers"),
                Ch("UC3", "Rick Astley Official", customUrl: "@rickastley"),
                Ch("UC4", "Rick Astley")
            };

            Assert.Equal(new[] { "UC4", "UC1", "UC2", "UC3" }, Utils.OrderChannelsByMatch("Rick Astley", channels).Select(c => c.Id));
            Assert.Equal(new[] { "UC3", "UC1", "UC2", "UC4" }, Utils.OrderChannelsByMatch("@rickastley", channels).Select(c => c.Id));
            Assert.Equal(new[] { "UC1", "UC2", "UC3", "UC4" }, Utils.OrderChannelsByMatch("", channels).Select(c => c.Id));
        }

        [Fact]
        public void OrderVideosByMatch_MovesExactTitleMatchesFirst()
        {
            var videos = new[] { Vid("v1", "Never Gonna Give You Up (Live)"), Vid("v2", "never gonna give you up") };

            Assert.Equal(new[] { "v2", "v1" }, Utils.OrderVideosByMatch("Never Gonna Give You Up", videos).Select(v => v.Id));
        }

        [Fact]
        public void PickBestVideoMatch_ReturnsExactTitleMatch_AnywhereInList()
        {
            var videos = new[] { Vid("v1", "Never Gonna Give You Up (Live)"), Vid("v2", "Never Gonna Give You Up") };

            Assert.Equal("v2", Utils.PickBestVideoMatch("never_gonna_give_you_up", null, videos)!.Id);
        }

        [Fact]
        public void PickBestVideoMatch_AcceptsTopHit_WhenPublishedOnFileDate()
        {
            var videos = new[]
            {
                Vid("v1", "Rick Astley - Never Gonna Give You Up (Official Video)", published: new DateTimeOffset(2009, 10, 25, 23, 59, 0, TimeSpan.Zero)),
                Vid("v2", "Never Gonna Give You Up", published: new DateTimeOffset(2009, 10, 25, 1, 0, 0, TimeSpan.Zero))
            };

            // v2 is exact, so it wins regardless of date...
            Assert.Equal("v2", Utils.PickBestVideoMatch("Never Gonna Give You Up", new DateTime(2009, 10, 25), videos)!.Id);

            // ...but with no exact match, the top hit is only accepted on a date match.
            var noExact = new[] { videos[0] };
            Assert.Equal("v1", Utils.PickBestVideoMatch("Never Gonna Give You Up", new DateTime(2009, 10, 25), noExact)!.Id);
            Assert.Null(Utils.PickBestVideoMatch("Never Gonna Give You Up", new DateTime(2009, 10, 26), noExact));
            Assert.Null(Utils.PickBestVideoMatch("Never Gonna Give You Up", null, noExact));
        }

        [Fact]
        public void PickBestVideoMatch_ReturnsNull_ForNoCandidates()
        {
            Assert.Null(Utils.PickBestVideoMatch("anything", null, Array.Empty<Video>()));
        }

        [Theory]
        [InlineData("\"rick astley\" music 80s", new[] { "rick astley", "music", "80s" })]
        [InlineData("music   pop \"never gonna give you up\"", new[] { "music", "pop", "never gonna give you up" })]
        [InlineData("music Music MUSIC", new[] { "music" })]
        [InlineData("\"unterminated quote", new[] { "unterminated quote" })]
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        public void ParseChannelKeywords(string? keywords, string[] expected)
        {
            Assert.Equal(expected, Utils.ParseChannelKeywords(keywords));
        }

        [Theory]
        [InlineData("https://en.wikipedia.org/wiki/Music", "Music")]
        [InlineData("https://en.wikipedia.org/wiki/Pop_music", "Pop music")]
        [InlineData("https://en.wikipedia.org/wiki/Lifestyle_(sociology)", "Lifestyle")]
        [InlineData("https://en.wikipedia.org/wiki/Role-playing_video_game", "Role-playing video game")]
        [InlineData("https://en.wikipedia.org/wiki/Caf%C3%A9", "Café")]
        [InlineData("https://en.wikipedia.org/", null)]
        [InlineData("not a url", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void TopicCategoryToGenre(string? url, string? expected)
        {
            Assert.Equal(expected, Utils.TopicCategoryToGenre(url));
        }

        [Fact]
        public void TopicCategoriesToGenres_DeduplicatesAndDropsJunk()
        {
            var genres = Utils.TopicCategoriesToGenres(new[]
            {
                "https://en.wikipedia.org/wiki/Music",
                "https://en.wikipedia.org/wiki/Pop_music",
                "https://en.wikipedia.org/wiki/music",
                "garbage"
            });

            Assert.Equal(new[] { "Music", "Pop music" }, genres);
            Assert.Empty(Utils.TopicCategoriesToGenres(null));
        }

        [Theory]
        [InlineData("US", "United States")]
        [InlineData("gb", "United Kingdom")]
        [InlineData("XX", "XX")]
        public void CountryCodeToName(string code, string expected)
        {
            Assert.Equal(expected, Utils.CountryCodeToName(code));
        }

        [Fact]
        public void GetBannerUrl_AppendsSizeSuffix_OrReturnsNull()
        {
            var channel = new Channel { BrandingSettings = new ChannelBrandingSettings { Image = new ImageSettings { BannerExternalUrl = "https://x/banner" } } };

            Assert.Equal("https://x/banner" + Constants.BannerSuffix, Utils.GetBannerUrl(channel, Constants.BannerSuffix));
            Assert.Null(Utils.GetBannerUrl(new Channel(), Constants.BannerSuffix));
            Assert.Null(Utils.GetBannerUrl(null, Constants.BannerSuffix));
        }

        [Fact]
        public void GetChannelHomePageUrl_PrefersHandle()
        {
            Assert.Equal("https://www.youtube.com/@rickastley", Utils.GetChannelHomePageUrl(Ch("UC1", "Rick", customUrl: "@rickastley")));
            Assert.Equal("https://www.youtube.com/channel/UC1", Utils.GetChannelHomePageUrl(Ch("UC1", "Rick")));
        }

        private static SearchResult Hit(string channelId, string title)
            => new() { Id = new ResourceId { ChannelId = channelId }, Snippet = new SearchResultSnippet { Title = title } };

        private static Channel Ch(string id, string title, string? customUrl = null)
            => new() { Id = id, Snippet = new ChannelSnippet { Title = title, CustomUrl = customUrl } };

        private static Video Vid(string id, string title, DateTimeOffset? published = null)
            => new() { Id = id, Snippet = new VideoSnippet { Title = title, PublishedAtDateTimeOffset = published } };
    }

    public class ConstantsTests
    {
        [Fact]
        public void PluginGuidIsValid()
        {
            Assert.True(System.Guid.TryParse(Constants.PluginGuid, out _));
        }

        [Fact]
        public void VideoUrlFormatsId()
        {
            Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", string.Format(Constants.VideoUrl, "dQw4w9WgXcQ"));
        }

        [Fact]
        public void ChannelUrlFormatsId()
        {
            Assert.Equal("https://www.youtube.com/channel/UCYO_jab_esuFRV4b17AJtAw", string.Format(Constants.ChannelUrl, "UCYO_jab_esuFRV4b17AJtAw"));
        }
    }

    public class UtilsMappingTests
    {
        [Fact]
        public void VideoToEpisode_MapsCoreFields()
        {
            var video = new Video
            {
                Id = "dQw4w9WgXcQ",
                Snippet = new VideoSnippet
                {
                    Title = "Never Gonna Give You Up",
                    Description = "The official video.",
                    ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw",
                    ChannelTitle = "Rick Astley",
                    PublishedAtDateTimeOffset = new DateTimeOffset(2009, 10, 25, 6, 57, 33, TimeSpan.Zero),
                    Tags = new List<string> { "80s", "pop" }
                },
                ContentDetails = new VideoContentDetails { Duration = "PT3M33S" },
                TopicDetails = new VideoTopicDetails { TopicCategories = new List<string> { "https://en.wikipedia.org/wiki/Pop_music" } }
            };

            var result = Utils.VideoToEpisode(video);

            Assert.True(result.HasMetadata);
            Assert.Equal("Never Gonna Give You Up", result.Item.Name);
            Assert.Equal("The official video.", result.Item.Overview);
            Assert.Equal(2009, result.Item.ProductionYear);
            Assert.Equal(new DateTime(2009, 10, 25, 6, 57, 33, DateTimeKind.Utc), result.Item.PremiereDate);
            Assert.Equal("20091025-Never Gonna Give You Up", result.Item.ForcedSortName);
            Assert.Null(result.Item.IndexNumber);
            Assert.Null(result.Item.ParentIndexNumber);
            Assert.Equal("dQw4w9WgXcQ", result.Item.ProviderIds[Constants.PluginName]);
            Assert.Equal(TimeSpan.FromSeconds(213).Ticks, result.Item.RunTimeTicks);
            Assert.Contains("80s", result.Item.Tags);
            Assert.Equal(new[] { "Pop music" }, result.Item.Genres);

            Assert.NotNull(result.People);
            Assert.Equal("Rick Astley", result.People[0].Name);
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", result.People[0].ProviderIds[Constants.PluginName]);
        }

        [Fact]
        public void VideoToEpisode_IgnoresUnparsableDuration()
        {
            var video = new Video
            {
                Id = "liveVideoId",
                Snippet = new VideoSnippet { Title = "Live stream" },
                ContentDetails = new VideoContentDetails { Duration = "P0D" }
            };

            var result = Utils.VideoToEpisode(video);

            Assert.True(result.HasMetadata);
            Assert.Null(result.Item.RunTimeTicks);
            Assert.Empty(result.Item.Genres);
        }

        [Fact]
        public void VideoToEpisode_AddsChannelAsPerson_EvenWithoutChannelId()
        {
            var video = new Video { Id = "v", Snippet = new VideoSnippet { Title = "T", ChannelTitle = "Rick Astley" } };

            var result = Utils.VideoToEpisode(video);

            Assert.Equal("Rick Astley", Assert.Single(result.People).Name);
        }

        [Fact]
        public void ChannelToSeries_MapsCoreFields()
        {
            var channel = new Channel
            {
                Id = "UCuAXFkgsw1L7xaCfnd5JJOw",
                Snippet = new ChannelSnippet
                {
                    Title = "Rick Astley",
                    Description = "The official channel.",
                    PublishedAtDateTimeOffset = new DateTimeOffset(2006, 3, 14, 0, 0, 0, TimeSpan.Zero)
                }
            };

            var result = Utils.ChannelToSeries(channel);

            Assert.True(result.HasMetadata);
            Assert.Equal("Rick Astley", result.Item.Name);
            Assert.Equal("The official channel.", result.Item.Overview);
            Assert.Equal("UCuAXFkgsw1L7xaCfnd5JJOw", result.Item.ProviderIds[Constants.PluginName]);
            Assert.Equal(2006, result.Item.ProductionYear);
            Assert.Equal(new DateTime(2006, 3, 14, 0, 0, 0, DateTimeKind.Utc), result.Item.PremiereDate);
            Assert.Equal("https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw", result.Item.HomePageUrl);
            Assert.Empty(result.Item.Tags);
            Assert.Empty(result.Item.Genres);
            Assert.Empty(result.Item.ProductionLocations);
        }

        [Fact]
        public void ChannelToSeries_MapsEveryExtendedPart()
        {
            var channel = new Channel
            {
                Id = "UCuAXFkgsw1L7xaCfnd5JJOw",
                Snippet = new ChannelSnippet
                {
                    Title = "Rick Astley",
                    Description = "The official channel.",
                    CustomUrl = "@rickastleyyt",
                    Country = "GB"
                },
                BrandingSettings = new ChannelBrandingSettings
                {
                    Channel = new ChannelSettings { Keywords = "\"rick astley\" music 80s" }
                },
                TopicDetails = new ChannelTopicDetails
                {
                    TopicCategories = new List<string>
                    {
                        "https://en.wikipedia.org/wiki/Music",
                        "https://en.wikipedia.org/wiki/Pop_music"
                    }
                }
            };

            var result = Utils.ChannelToSeries(channel);

            Assert.Equal(new[] { "rick astley", "music", "80s" }, result.Item.Tags);
            Assert.Equal(new[] { "Music", "Pop music" }, result.Item.Genres);
            Assert.Equal(new[] { "United Kingdom" }, result.Item.ProductionLocations);
            Assert.Equal("https://www.youtube.com/@rickastleyyt", result.Item.HomePageUrl);
        }

        [Fact]
        public void ChannelToSeries_FallsBackToBrandingCountry()
        {
            var channel = new Channel
            {
                Id = "UC1",
                Snippet = new ChannelSnippet { Title = "T" },
                BrandingSettings = new ChannelBrandingSettings { Channel = new ChannelSettings { Country = "US" } }
            };

            Assert.Equal(new[] { "United States" }, Utils.ChannelToSeries(channel).Item.ProductionLocations);
        }

        [Fact]
        public void ChannelToSearchResult_CarriesEverythingIdentifyCanShow()
        {
            var channel = new Channel
            {
                Id = "UC1",
                Snippet = new ChannelSnippet
                {
                    Title = "Rick Astley",
                    Description = "Desc",
                    PublishedAtDateTimeOffset = new DateTimeOffset(2006, 3, 14, 0, 0, 0, TimeSpan.Zero),
                    Thumbnails = new ThumbnailDetails { Medium = new Thumbnail { Url = "medium.jpg" } }
                }
            };

            var result = Utils.ChannelToSearchResult(channel);

            Assert.Equal("Rick Astley", result.Name);
            Assert.Equal("Desc", result.Overview);
            Assert.Equal(2006, result.ProductionYear);
            Assert.Equal(new DateTime(2006, 3, 14, 0, 0, 0, DateTimeKind.Utc), result.PremiereDate);
            Assert.Equal("medium.jpg", result.ImageUrl);
            Assert.Equal("UC1", result.ProviderIds[Constants.PluginName]);
            Assert.Equal(Constants.PluginName, result.SearchProviderName);
        }

        [Fact]
        public void VideoToSearchResult_CarriesEverythingIdentifyCanShow()
        {
            var video = new Video
            {
                Id = "dQw4w9WgXcQ",
                Snippet = new VideoSnippet
                {
                    Title = "Never Gonna Give You Up",
                    Description = "Desc",
                    PublishedAtDateTimeOffset = new DateTimeOffset(2009, 10, 25, 6, 57, 33, TimeSpan.Zero),
                    Thumbnails = new ThumbnailDetails { Maxres = new Thumbnail { Url = "maxres.jpg" } }
                }
            };

            var result = Utils.VideoToSearchResult(video);

            Assert.Equal("Never Gonna Give You Up", result.Name);
            Assert.Equal("Desc", result.Overview);
            Assert.Equal(2009, result.ProductionYear);
            Assert.Equal("maxres.jpg", result.ImageUrl);
            Assert.Equal("dQw4w9WgXcQ", result.ProviderIds[Constants.PluginName]);
            Assert.Equal(Constants.PluginName, result.SearchProviderName);
        }

        [Fact]
        public void GetBestThumbnailUrl_PrefersHighestResolution()
        {
            var thumbnails = new ThumbnailDetails
            {
                Default__ = new Thumbnail { Url = "default.jpg" },
                High = new Thumbnail { Url = "high.jpg" }
            };

            Assert.Equal("high.jpg", Utils.GetBestThumbnailUrl(thumbnails));
        }

        [Fact]
        public void GetBestThumbnailUrl_ReturnsNullWhenNoThumbnails()
        {
            Assert.Null(Utils.GetBestThumbnailUrl(null));
        }
    }

    public class ComputeEpisodeIndexTests
    {
        [Fact]
        public void ReturnsOne_WhenNoSiblings()
        {
            var index = Utils.ComputeEpisodeIndex(
                "current1111",
                new DateTime(2024, 6, 15),
                Array.Empty<(string, DateTime)>());

            Assert.Equal(1, index);
        }

        [Fact]
        public void RanksByPremiereDate_AcrossAllSiblings_RegardlessOfYear()
        {
            var siblings = new[]
            {
                ("olderVid111", new DateTime(2020, 1, 1)),
                ("newerVid111", new DateTime(2025, 12, 1))
            };

            var index = Utils.ComputeEpisodeIndex("current1111", new DateTime(2024, 6, 15), siblings);

            Assert.Equal(2, index);
        }

        [Fact]
        public void ExcludesItselfFromSiblingList()
        {
            var siblings = new[]
            {
                ("current1111", new DateTime(2024, 1, 1)),
                ("otherVid1111", new DateTime(2024, 3, 1))
            };

            var index = Utils.ComputeEpisodeIndex("current1111", new DateTime(2024, 6, 15), siblings);

            Assert.Equal(2, index);
        }

        [Fact]
        public void BreaksTiesOnSameDate_ByVideoIdOrdinal()
        {
            var indexAfter = Utils.ComputeEpisodeIndex(
                "zzzzzzzzzzz",
                new DateTime(2024, 6, 15),
                new[] { ("aaaaaaaaaaa", new DateTime(2024, 6, 15)) });

            var indexBefore = Utils.ComputeEpisodeIndex(
                "aaaaaaaaaaa",
                new DateTime(2024, 6, 15),
                new[] { ("zzzzzzzzzzz", new DateTime(2024, 6, 15)) });

            Assert.Equal(2, indexAfter);
            Assert.Equal(1, indexBefore);
        }

        [Fact]
        public void NeverRepeatsAcrossManySiblings()
        {
            var siblings = Enumerable.Range(0, 20)
                .Select(i => ($"vid{i:D8}", new DateTime(2020, 1, 1).AddDays(i)))
                .ToArray();

            var indexes = siblings
                .Select(s => Utils.ComputeEpisodeIndex(s.Item1, s.Item2, siblings))
                .ToList();

            Assert.Equal(indexes.Count, indexes.Distinct().Count());
        }
    }
}
