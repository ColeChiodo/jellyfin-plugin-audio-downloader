using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.AudioDownloader;

/// <summary>
/// Registers the plugin's services with the dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AudioProcessor>();
        serviceCollection.AddSingleton<DownloadJobService>();
        serviceCollection.AddSingleton<IHostedService, WebBootstrapService>();
    }
}
