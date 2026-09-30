const YoutubeApiMetadataConfig = {
    pluginUniqueId: '338cccea-4c27-474e-8934-4c7c3737d034'
};

function clampInt(value, fallback, min, max) {
    const parsed = parseInt(value, 10);
    if (isNaN(parsed)) {
        return fallback;
    }
    return Math.min(max, Math.max(min, parsed));
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;

        ApiClient.getPluginConfiguration(YoutubeApiMetadataConfig.pluginUniqueId).then(function (config) {
            page.querySelector('#apiKey').value = config.ApiKey || '';
            page.querySelector('#cacheExpirationDays').value = config.CacheExpirationDays;
            page.querySelector('#enableChannelNameSearch').checked = config.EnableChannelNameSearch !== false;
            page.querySelector('#enableTitleSearchFallback').checked = config.EnableTitleSearchFallback !== false;
            page.querySelector('#searchResultLimit').value = config.SearchResultLimit || 10;
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: 'Failed to load plugin configuration' });
        });
    });

    view.querySelector('#YoutubeApiMetadataConfigForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;

        ApiClient.getPluginConfiguration(YoutubeApiMetadataConfig.pluginUniqueId).then(function (config) {
            config.ApiKey = form.querySelector('#apiKey').value.trim();
            config.CacheExpirationDays = clampInt(form.querySelector('#cacheExpirationDays').value, 30, 1, 3650);
            config.EnableChannelNameSearch = form.querySelector('#enableChannelNameSearch').checked;
            config.EnableTitleSearchFallback = form.querySelector('#enableTitleSearchFallback').checked;
            config.SearchResultLimit = clampInt(form.querySelector('#searchResultLimit').value, 10, 1, 50);

            ApiClient.updatePluginConfiguration(YoutubeApiMetadataConfig.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.processErrorResponse({ statusText: 'Failed to update plugin configuration' });
            });
        }).catch(function () {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: 'Failed to load plugin configuration' });
        });

        return false;
    });
}
