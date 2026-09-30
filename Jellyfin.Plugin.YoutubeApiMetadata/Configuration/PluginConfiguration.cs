using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets the YouTube Data API v3 key, created in Google Cloud Console.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the number of days a cached API response stays valid before being refreshed.
        /// </summary>
        public int CacheExpirationDays { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a series folder with no "[channelId]" in its name
        /// is resolved by searching YouTube for the folder name (or handle). Costs 100 quota units
        /// per folder, once; the result is cached and persisted on the item.
        /// </summary>
        public bool EnableChannelNameSearch { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a video file with no "[videoId]" in its name is
        /// resolved by searching its channel for the file's title. Costs 100 quota units per file,
        /// once; only exact title matches (or a top hit uploaded on the file's date) are accepted.
        /// </summary>
        public bool EnableTitleSearchFallback { get; set; }

        /// <summary>
        /// Gets or sets how many candidates Jellyfin's manual "Identify" dialog shows for a
        /// channel or video search (1–50).
        /// </summary>
        public int SearchResultLimit { get; set; }

        public PluginConfiguration()
        {
            ApiKey = string.Empty;
            CacheExpirationDays = 30;
            EnableChannelNameSearch = true;
            EnableTitleSearchFallback = true;
            SearchResultLimit = 10;
        }
    }
}
