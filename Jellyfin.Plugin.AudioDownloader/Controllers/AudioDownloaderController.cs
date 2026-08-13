using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDownloader.Configuration;
using Jellyfin.Plugin.AudioDownloader.Models;
using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.AudioDownloader.Controllers;

/// <summary>
/// Exposes the audio downloader functionality to the web client.
/// </summary>
[ApiController]
[Route("AudioDownloader")]
[Authorize]
public class AudioDownloaderController : ControllerBase
{
    private readonly AudioProcessor _audioProcessor;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDownloaderController"/> class.
    /// </summary>
    /// <param name="audioProcessor">The audio processing service.</param>
    /// <param name="libraryManager">The library manager.</param>
    public AudioDownloaderController(AudioProcessor audioProcessor, ILibraryManager libraryManager)
    {
        _audioProcessor = audioProcessor;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Serves the client side script injected into the web interface.
    /// </summary>
    /// <returns>The javascript bundle.</returns>
    [AllowAnonymous]
    [HttpGet("script")]
    public ActionResult GetScript()
    {
        var stream = GetType().Assembly.GetManifestResourceStream("Jellyfin.Plugin.AudioDownloader.Web.audioDownloader.js");
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Lists the audio tracks available for an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The available audio tracks.</returns>
    [HttpGet("tracks")]
    public ActionResult<IReadOnlyList<AudioTrackDto>> GetAudioTracks([FromQuery] Guid itemId)
    {
        if (!AssertUserAllowed(out var forbidden))
        {
            return forbidden;
        }

        var item = _libraryManager.GetItemById<BaseItem>(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var items = _audioProcessor.ResolveTargetItems(item);
        return Ok(_audioProcessor.GetAudioTracks(items));
    }

    /// <summary>
    /// Renders and downloads the compressed audio track for an item.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="stream">The audio stream index to use, or <c>-1</c> for the default.</param>
    /// <param name="format">The output format, <c>mp3</c> or <c>m4a</c>.</param>
    /// <returns>The rendered audio file.</returns>
    [HttpGet("download")]
    public async Task<ActionResult> DownloadAudio(
        [FromQuery] Guid itemId,
        [FromQuery] int? stream = null,
        [FromQuery] string? format = null)
    {
        if (!AssertUserAllowed(out var forbidden))
        {
            return forbidden;
        }

        var item = _libraryManager.GetItemById<BaseItem>(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var outputFormat = ParseFormat(format) ?? config.DefaultFormat;
        var items = _audioProcessor.ResolveTargetItems(item);
        if (items.Count == 0)
        {
            return BadRequest("No playable items found.");
        }

        AudioDownloadResult result;
        try
        {
            result = await _audioProcessor
                .BuildAudioFileAsync(items, stream ?? -1, outputFormat, config, HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Download cancelled.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var extension = outputFormat == AudioFormat.Mpeg3 ? "mp3" : "m4a";
        var contentType = outputFormat == AudioFormat.Mpeg3 ? "audio/mpeg" : "audio/mp4";
        var title = SanitizeFileName(item.Name) ?? $"audio-{item.Id.ToString("N", CultureInfo.InvariantCulture)}";

        var fileStream = new FileStream(
            result.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            262_144,
            FileOptions.DeleteOnClose);

        // Dispose order is LIFO: the temp directory cleanup is registered first so the file
        // stream is closed (and the file unlinked via DeleteOnClose) before the directory is removed.
        HttpContext.Response.RegisterForDispose(new TempDirectoryCleanup(result.TempDirectory));
        return File(
            fileStream,
            contentType,
            string.Format(CultureInfo.InvariantCulture, "{0}.{1}", title, extension));
    }

    private bool AssertUserAllowed(out ActionResult forbidden)
    {
        if (Plugin.Instance?.Configuration is { } config && config.AdminOnly && !User.IsInRole("Administrator"))
        {
            forbidden = Forbid();
            return false;
        }

        forbidden = null!;
        return true;
    }

    private static AudioFormat? ParseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "mp3" => AudioFormat.Mpeg3,
            "m4a" or "aac" => AudioFormat.M4A,
            _ => null
        };
    }

    private static string? SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private sealed class TempDirectoryCleanup : IDisposable
    {
        private readonly string _path;

        public TempDirectoryCleanup(string path)
        {
            _path = path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_path))
                {
                    Directory.Delete(_path, true);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }
    }
}
