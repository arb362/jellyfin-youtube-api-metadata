using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Jellyfin.Plugin.YoutubeApiMetadata.Caching;
using Jellyfin.Plugin.YoutubeApiMetadata.Configuration;
using Jellyfin.Plugin.YoutubeApiMetadata.YouTube;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.YoutubeApiMetadata
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => Constants.PluginName;

        public override Guid Id => Guid.Parse(Constants.PluginGuid);

        private readonly IHttpClientFactory _httpClientFactory;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IHttpClientFactory httpClientFactory) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _httpClientFactory = httpClientFactory;
        }

        public static Plugin Instance { get; private set; } = null!;

        public HttpClient GetHttpClient()
        {
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("YTApiMetadata", Version.ToString()));

            return httpClient;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
                },
                new PluginPageInfo
                {
                    Name = "youtubeapimetadataconfigjs",
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.js", GetType().Namespace)
                }
            };
        }
    }

    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Providers read settings through this accessor (rather than Plugin.Instance directly)
            // so they always see the *current* configuration and can be unit-tested without a
            // running Jellyfin host.
            serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance.Configuration);
            serviceCollection.AddTransient<IYouTubeApiClient>(_ => new YouTubeApiClient(Plugin.Instance.Configuration.ApiKey));
            serviceCollection.AddSingleton<IMetadataCache>(sp => new FileMetadataCache(
                sp.GetRequiredService<IApplicationPaths>(),
                () => Plugin.Instance.Configuration.CacheExpirationDays));
            serviceCollection.AddTransient<IYoutubeMetadataResolver, YoutubeMetadataResolver>();
        }
    }
}
