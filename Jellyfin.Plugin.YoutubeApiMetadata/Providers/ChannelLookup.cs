using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;

namespace Jellyfin.Plugin.YoutubeApiMetadata.Providers
{
    /// <summary>
    /// The one place that decides which channel a Series item is, shared by the metadata and image
    /// providers so they can never disagree.
    /// </summary>
    internal static class ChannelLookup
    {
        /// <summary>
        /// Resolves a series' channel from, in order:
        /// 1. the value stored under the plugin's provider key — a channel ID, or a handle, URL or
        ///    name the user typed into the "YouTube" external ID field (explicit user input, so it
        ///    is honoured even when automatic name search is switched off);
        /// 2. a "[channelId]" in the folder name (or the display name);
        /// 3. when <paramref name="allowNameSearch"/> is on, the folder name itself.
        /// </summary>
        public static async Task<Channel?> ResolveAsync(
            IYoutubeMetadataResolver resolver,
            IReadOnlyDictionary<string, string>? providerIds,
            string? path,
            string? name,
            bool allowNameSearch,
            CancellationToken cancellationToken)
        {
            var stored = Utils.ParseChannelReference(Utils.GetStoredProviderValue(providerIds));
            if (stored.Kind == ChannelReferenceKind.Id)
            {
                return await resolver.GetChannelAsync(stored.Value, cancellationToken).ConfigureAwait(false);
            }

            if (stored.Kind is ChannelReferenceKind.Handle or ChannelReferenceKind.Name)
            {
                var explicitMatch = await resolver.FindChannelByNameAsync(stored.Value, cancellationToken).ConfigureAwait(false);
                if (explicitMatch != null)
                {
                    return explicitMatch;
                }
            }

            var channelId = Utils.ResolveChannelId(null, path, name);
            if (!string.IsNullOrEmpty(channelId))
            {
                return await resolver.GetChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            }

            if (!allowNameSearch)
            {
                return null;
            }

            // Prefer the raw folder name over Jellyfin's parsed Name: the parser can strip pieces a
            // channel name legitimately contains (a trailing year in parentheses, for instance).
            var folderName = Utils.GetChannelNameFromPath(path) ?? name;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return null;
            }

            return await resolver.FindChannelByNameAsync(folderName, cancellationToken).ConfigureAwait(false);
        }
    }
}
