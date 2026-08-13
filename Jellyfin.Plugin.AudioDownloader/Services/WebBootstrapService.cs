using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDownloader.Services;

/// <summary>
/// Injects a script tag referencing the plugin's client bundle into the web client's index.html.
/// This is the established community mechanism for plugins to extend the web UI.
/// </summary>
public sealed class WebBootstrapService : IHostedService
{
    private const string ScriptTag = "<script id=\"audio-downloader\" src=\"../../AudioDownloader/script\" defer></script>";

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<WebBootstrapService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebBootstrapService"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public WebBootstrapService(IApplicationPaths applicationPaths, ILogger<WebBootstrapService> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(_applicationPaths.WebPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _logger.LogDebug(
                "Web client index.html not found at {Path}, skipping script injection",
                indexPath);
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            if (content.Contains(ScriptTag, StringComparison.Ordinal))
            {
                return;
            }

            var bodyIndex = content.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex < 0)
            {
                _logger.LogWarning(
                    "Could not locate </body> in {Path}, skipping script injection",
                    indexPath);
                return;
            }

            var updated = content.Insert(bodyIndex, ScriptTag + Environment.NewLine);
            await File.WriteAllTextAsync(indexPath, updated, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Injected audio downloader script into {Path}", indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "Failed to inject the audio downloader script into {Path}. In restricted environments the web directory may need write access.",
                indexPath);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
