namespace Jellyfin.Plugin.YoutubeApiMetadata
{
    public class Constants
    {
        public const string PluginName = "YoutubeApiMetadata";
        public const string PluginGuid = "338cccea-4c27-474e-8934-4c7c3737d034";
        public const string ChannelUrl = "https://www.youtube.com/channel/{0}";
        public const string VideoUrl = "https://www.youtube.com/watch?v={0}";

        /// <summary>
        /// Canonical URL for a channel handle ("@name"), the value of the API's snippet.customUrl.
        /// </summary>
        public const string HandleUrl = "https://www.youtube.com/{0}";

        /// <summary>
        /// Cache subdirectory name under Jellyfin's CachePath. Deliberately different from the
        /// old ankenyr/jellyfin-youtube-metadata-plugin's "youtubemetadata" folder so both plugins
        /// can run side by side without fighting over the same cache files.
        /// </summary>
        public const string CacheDirectoryName = "youtubeapimetadata";

        /// <summary>
        /// Subdirectory (under <see cref="CacheDirectoryName"/>) holding channel-name → channel-ID
        /// lookups, so a name search (100 quota units) is never repeated for the same folder name.
        /// </summary>
        public const string ChannelNameCacheDirectoryName = "channel-names";

        public const string YTCHANNEL_RE = @"(?<=\[)[a-zA-Z0-9\-_]{24}(?=\])";
        public const string YTID_RE = @"(?<=\[)[a-zA-Z0-9\-_]{11}(?=\])";

        /// <summary>
        /// A YouTube handle as it appears in URLs and in the API's snippet.customUrl: "@" followed by
        /// 3–30 letters, digits, underscores, hyphens or periods.
        /// </summary>
        public const string YTHANDLE_RE = @"^@[a-zA-Z0-9_\-.]{3,30}$";

        /// <summary>
        /// Maximum number of IDs the YouTube Data API accepts in one videos.list/channels.list call.
        /// </summary>
        public const int MaxIdsPerListRequest = 50;

        /// <summary>
        /// Size suffixes appended to brandingSettings.image.bannerExternalUrl to get a rendered
        /// banner at a given width. The fcrop64 value is YouTube's own "TV" (16:9-ish) crop; without
        /// a suffix the URL returns the raw uploaded artwork, which can be any aspect ratio.
        /// </summary>
        public const string BannerBackdropSuffix = "=w2560-fcrop64=1,00005a57ffffa5a8-k-c0xffffffff-no-nd-rj";
        public const string BannerSuffix = "=w2120-fcrop64=1,00005a57ffffa5a8-k-c0xffffffff-no-nd-rj";
    }
}
