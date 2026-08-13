using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
    private readonly DownloadJobService _jobService;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDownloaderController"/> class.
    /// </summary>
    /// <param name="audioProcessor">The audio processing service.</param>
    /// <param name="jobService">The download job tracking service.</param>
    /// <param name="libraryManager">The library manager.</param>
    public AudioDownloaderController(AudioProcessor audioProcessor, DownloadJobService jobService, ILibraryManager libraryManager)
    {
        _audioProcessor = audioProcessor;
        _jobService = jobService;
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

        HttpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        HttpContext.Response.Headers.Pragma = "no-cache";
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
    /// Starts rendering the compressed audio track for an item. The response returns a job
    /// identifier; poll <see cref="GetDownloadProgress"/> until it reports Ready, then
    /// fetch <see cref="DownloadPreparedFile"/>.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="stream">The audio stream index to use, or <c>-1</c> for the default.</param>
    /// <param name="format">The output format, <c>mp3</c> or <c>m4a</c>.</param>
    /// <returns>A job identifier.</returns>
    [HttpGet("prepare")]
    public async Task<ActionResult> PrepareDownload(
        [FromQuery] Guid itemId,
        [FromQuery] int? stream = null,
        [FromQuery] string? format = null)
    {
        if (!AssertUserAllowed(out var forbidden))
        {
            return forbidden;
        }

        if (_jobService.PreparingCount() >= 2)
        {
            return StatusCode(429, "Too many audio downloads are already being prepared.");
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

        var jobId = _jobService.Create();
        var progress = new Progress<AudioProgressInfo>(info => _jobService.Update(jobId, info));

        try
        {
            var result = await _audioProcessor
                .BuildAudioFileAsync(items, stream ?? -1, outputFormat, config, CancellationToken.None, progress)
                .ConfigureAwait(false);

            var downloadName = AudioProcessor.BuildDownloadFileName(item);
            _jobService.Complete(jobId, result.FilePath, result.TempDirectory, downloadName);
            return Ok(new { jobId });
        }
        catch (InvalidOperationException ex)
        {
            _jobService.Fail(jobId, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _jobService.Fail(jobId, ex.ToString());
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// Gets the progress of a download job.
    /// </summary>
    /// <param name="jobId">The job id from <see cref="PrepareDownload"/>.</param>
    /// <returns>The progress snapshot.</returns>
    [HttpGet("prepare/{jobId:guid}")]
    public ActionResult GetDownloadProgress(Guid jobId)
    {
        var snapshot = _jobService.GetProgress(jobId);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(snapshot);
    }

    /// <summary>
    /// Streams the finished audio file for a completed download job.
    /// </summary>
    /// <param name="jobId">The job id from <see cref="PrepareDownload"/>.</param>
    /// <returns>The rendered audio file.</returns>
    [HttpGet("download/{jobId:guid}")]
    public ActionResult DownloadPreparedFile(Guid jobId)
    {
        if (!AssertUserAllowed(out var forbidden))
        {
            return forbidden;
        }

        // CA3003: jobId only selects an entry created and owned by the job service; the file
        // path below originates from our internal temp store, never from user input.
#pragma warning disable CA3003 // Review code for file path injection vulnerabilities
        if (!_jobService.TryClaimReadyJob(jobId, out var outputPath, out var tempDirectory, out var downloadName))
        {
            return StatusCode(202, "The audio is still being prepared.");
        }

        if (string.IsNullOrWhiteSpace(outputPath) || !System.IO.File.Exists(outputPath))
        {
            return StatusCode(202, "The audio is still being prepared.");
        }

        var extension = Path.GetExtension(outputPath).TrimStart('.');
        var contentType = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" : "audio/mp4";

        var fileStream = new FileStream(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            262_144,
            FileOptions.DeleteOnClose);

        // Dispose order is LIFO: the temp directory cleanup is registered first so the file
        // stream is closed (and the file unlinked via DeleteOnClose) before the directory is removed.
        HttpContext.Response.RegisterForDispose(new TempDirectoryCleanup(tempDirectory));
#pragma warning restore CA3003
        return File(
            fileStream,
            contentType,
            string.Format(CultureInfo.InvariantCulture, "{0}.{1}", downloadName ?? "audio", extension));
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

    private sealed class TempDirectoryCleanup : IDisposable
    {
        private readonly string? _path;

        public TempDirectoryCleanup(string? path)
        {
            _path = path;
        }

        public void Dispose()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_path) && Directory.Exists(_path))
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
