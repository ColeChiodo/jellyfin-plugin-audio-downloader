using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.AudioDownloader.Configuration;
using Jellyfin.Plugin.AudioDownloader.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDownloader.Services;

/// <summary>
/// Core helper that renders an item's audio track to a compressed file while cutting segments
/// (intros, outros, commercials, previews) and long dead air.
/// </summary>
public sealed class AudioProcessor
{
    private static readonly SemaphoreSlim ConcurrencyGuard = new(2, 2);
    private static readonly Regex SilenceStartRegex = new(@"silence_start:\s*([\d.eE+-]+)", RegexOptions.Compiled);
    private static readonly Regex SilenceEndRegex = new(@"silence_end:\s*([\d.eE+-]+)", RegexOptions.Compiled);

    private readonly IMediaEncoder _mediaEncoder;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<AudioProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioProcessor"/> class.
    /// </summary>
    /// <param name="mediaEncoder">The media encoder.</param>
    /// <param name="mediaSegmentManager">The media segment manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public AudioProcessor(
        IMediaEncoder mediaEncoder,
        IMediaSegmentManager mediaSegmentManager,
        ILibraryManager libraryManager,
        IApplicationPaths applicationPaths,
        ILogger<AudioProcessor> logger)
    {
        _mediaEncoder = mediaEncoder;
        _mediaSegmentManager = mediaSegmentManager;
        _libraryManager = libraryManager;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the concrete items whose audio should be combined for the given item.
    /// A movie or episode resolves to itself; a season or series resolves to its episodes.
    /// </summary>
    /// <param name="item">The item to resolve.</param>
    /// <returns>The items whose audio tracks will be processed.</returns>
    public IReadOnlyList<BaseItem> ResolveTargetItems(BaseItem item)
    {
        if (item is Movie or Episode)
        {
            return [item];
        }

        if (item is Season or Series)
        {
            var query = new InternalItemsQuery
            {
                ParentId = item.Id,
                Recursive = true,
                IncludeItemTypes = new[] { BaseItemKind.Episode }
            };

            var episodes = _libraryManager.GetItemList(query)
                .OrderBy(e => e.ParentIndexNumber ?? int.MaxValue)
                .ThenBy(e => e.IndexNumber ?? int.MaxValue)
                .ToList();

            _logger.LogInformation(
                "Resolved {ItemName} to {Count} episodes",
                item.Name,
                episodes.Count);

            return episodes;
        }

        return [item];
    }

    /// <summary>
    /// Enumerates the union of audio tracks available on the given item scope.
    /// </summary>
    /// <param name="items">The items to inspect.</param>
    /// <returns>The distinct audio tracks, keyed by container stream index.</returns>
    public IReadOnlyList<AudioTrackDto> GetAudioTracks(IReadOnlyList<BaseItem> items)
    {
        var tracks = new Dictionary<int, AudioTrackDto>();

        foreach (var item in items)
        {
            var source = GetFirstMediaSource(item);
            if (source is null)
            {
                continue;
            }

            foreach (var stream in source.MediaStreams.Where(s => s.Type == MediaStreamType.Audio && !s.IsExternal))
            {
                if (tracks.TryGetValue(stream.Index, out var existing))
                {
                    existing.IsDefault = existing.IsDefault || stream.IsDefault;
                    continue;
                }

                tracks[stream.Index] = new AudioTrackDto
                {
                    Index = stream.Index,
                    Language = stream.Language,
                    Codec = stream.Codec,
                    Channels = stream.Channels,
                    ChannelLayout = stream.ChannelLayout,
                    IsDefault = stream.IsDefault,
                    DisplayTitle = BuildTrackTitle(stream)
                };
            }
        }

        return tracks.Values.OrderBy(t => t.Index).ToList();
    }

    /// <summary>
    /// Renders the combined audio for the target items to a file.
    /// </summary>
    /// <param name="items">The items to process.</param>
    /// <param name="audioStreamIndex">The desired audio stream index, or <c>-1</c> for the container default.</param>
    /// <param name="format">The output audio format.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="AudioDownloadResult"/> describing the rendered file.</returns>
    public async Task<AudioDownloadResult> BuildAudioFileAsync(
        IReadOnlyList<BaseItem> items,
        int audioStreamIndex,
        AudioFormat format,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(_applicationPaths.TempDirectory, "audiodownloader", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(workDir);

        await ConcurrencyGuard.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var extension = format == AudioFormat.Mpeg3 ? "mp3" : "m4a";
            var segmentFiles = new List<string>();
            var targetChannels = 2;

            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[i];
                var path = GetMediaFilePath(item);
                if (string.IsNullOrWhiteSpace(path))
                {
                    _logger.LogWarning("No playable media file found for {ItemName}, skipping", item.Name);
                    continue;
                }

                var source = GetFirstMediaSource(item);
                if (source is null)
                {
                    _logger.LogWarning("No media source found for {ItemName}, skipping", item.Name);
                    continue;
                }

                var audioStream = ResolveAudioStream(source.MediaStreams, audioStreamIndex, config.PreferredAudioLanguage);
                if (audioStream is null)
                {
                    _logger.LogWarning("No audio track available for {ItemName}, skipping", item.Name);
                    continue;
                }

                var audioPosition = IndexOfStream(source.MediaStreams, audioStream);
                if (i == 0)
                {
                    targetChannels = ComputeTargetChannels(audioStream.Channels, format, config.MaxChannels);
                }

                var removedIntervals = await BuildRemovedIntervalsAsync(item, config, path, audioPosition, cancellationToken).ConfigureAwait(false);

                var segmentFile = Path.Combine(workDir, string.Format(CultureInfo.InvariantCulture, "seg_{0}.{1}", i, extension));
                await EncodeSegmentAsync(path, audioPosition, removedIntervals, targetChannels, format, config, segmentFile, cancellationToken).ConfigureAwait(false);

                segmentFiles.Add(segmentFile);
            }

            if (segmentFiles.Count == 0)
            {
                throw new InvalidOperationException("No playable audio tracks were found for the requested item.");
            }

            var finalFile = Path.Combine(workDir, string.Format(CultureInfo.InvariantCulture, "output.{0}", extension));
            await ConcatAsync(segmentFiles, finalFile, targetChannels, format, config, cancellationToken).ConfigureAwait(false);

            foreach (var segment in segmentFiles)
            {
                TryDelete(segment);
            }

            return new AudioDownloadResult(finalFile, workDir);
        }
        finally
        {
            ConcurrencyGuard.Release();
        }
    }

    private async Task<IReadOnlyList<(double Start, double End)>> BuildRemovedIntervalsAsync(
        BaseItem item,
        PluginConfiguration config,
        string inputPath,
        int audioPosition,
        CancellationToken cancellationToken)
    {
        var intervals = new List<(double Start, double End)>();

        var segmentTypes = new List<MediaSegmentType>();
        if (config.IncludeIntros)
        {
            segmentTypes.Add(MediaSegmentType.Intro);
        }

        if (config.IncludeOutros)
        {
            segmentTypes.Add(MediaSegmentType.Outro);
        }

        if (config.IncludeCommercials)
        {
            segmentTypes.Add(MediaSegmentType.Commercial);
        }

        if (config.IncludePreviews)
        {
            segmentTypes.Add(MediaSegmentType.Preview);
        }

        if (config.IncludeRecaps)
        {
            segmentTypes.Add(MediaSegmentType.Recap);
        }

        if (segmentTypes.Count > 0)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            var segments = await _mediaSegmentManager
                .GetSegmentsAsync(item, segmentTypes, libraryOptions)
                .ConfigureAwait(false);

            foreach (var segment in segments)
            {
                var start = segment.StartTicks / 10_000_000.0;
                var end = segment.EndTicks / 10_000_000.0;
                if (end > start)
                {
                    intervals.Add((start, end));
                }
            }
        }

        if (config.SilenceDurationSeconds > 0)
        {
            intervals.AddRange(await DetectSilenceAsync(inputPath, audioPosition, config, cancellationToken).ConfigureAwait(false));
        }

        return MergeIntervals(intervals);
    }

    private async Task<IReadOnlyList<(double Start, double End)>> DetectSilenceAsync(
        string inputPath,
        int audioPosition,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "info",
            "-analyzeduration",
            "200M",
            "-probesize",
            "128M",
            "-i",
            inputPath,
            "-map",
            string.Format(CultureInfo.InvariantCulture, "0:{0}", audioPosition),
            "-vn",
            "-af",
            string.Format(CultureInfo.InvariantCulture, "silencedetect=n={0}dB:d={1:0.###}", config.SilenceThresholdDb, config.SilenceDurationSeconds),
            "-f",
            "null",
            "-"
        };

        var process = CreateProcess(_mediaEncoder.EncoderPath, args);
        var error = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.AppendLine(e.Data);
            }
        };

        _logger.LogInformation(
            "Running silence detection: {Path} {Arguments}",
            _mediaEncoder.EncoderPath,
            ArgsToStringSafe(args));

        var exitCode = 0;
        using (process)
        {
            try
            {
                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            exitCode = process.ExitCode;
        }

        if (exitCode != 0)
        {
            _logger.LogWarning(
                "Silence detection exited with code {ExitCode}: {Log}",
                exitCode,
                error.ToString());
        }

        var intervals = ParseSilenceIntervals(error.ToString());

        _logger.LogInformation("Detected {Count} silent intervals in {File}", intervals.Count, Path.GetFileName(inputPath));
        return intervals;
    }

    /// <summary>
    /// Extracts silent intervals from the stderr output produced by ffmpeg's silencedetect filter.
    /// </summary>
    /// <param name="output">The ffmpeg stderr output.</param>
    /// <returns>The detected silent intervals, with an unpaired trailing run open ended.</returns>
    internal static IReadOnlyList<(double Start, double End)> ParseSilenceIntervals(string output)
    {
        var starts = SilenceStartRegex.Matches(output).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        var ends = SilenceEndRegex.Matches(output).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();

        var intervals = new List<(double Start, double End)>();
        var count = Math.Min(starts.Count, ends.Count);
        for (var i = 0; i < count; i++)
        {
            if (ends[i] > starts[i])
            {
                intervals.Add((starts[i], ends[i]));
            }
        }

        // Unpaired trailing silence run.
        if (starts.Count > ends.Count)
        {
            intervals.Add((starts[^1], double.PositiveInfinity));
        }

        return intervals;
    }

    private async Task EncodeSegmentAsync(
        string inputPath,
        int audioPosition,
        IReadOnlyList<(double Start, double End)> removedIntervals,
        int targetChannels,
        AudioFormat format,
        PluginConfiguration config,
        string outputFile,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "info",
            "-y",
            "-analyzeduration",
            "200M",
            "-probesize",
            "128M",
            "-i",
            inputPath,
            "-map",
            string.Format(CultureInfo.InvariantCulture, "0:{0}", audioPosition),
            "-vn"
        };

        if (removedIntervals.Count > 0)
        {
            args.Add("-af");
            args.Add(BuildFilterGraph(removedIntervals));
        }

        args.Add("-ar");
        args.Add("48000");
        args.Add("-ac");
        args.Add(targetChannels.ToString(CultureInfo.InvariantCulture));

        if (format == AudioFormat.Mpeg3)
        {
            args.Add("-c:a");
            args.Add("libmp3lame");
            args.Add("-b:a");
            args.Add(string.Format(CultureInfo.InvariantCulture, "{0}k", config.Mp3Bitrate));
        }
        else
        {
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add(string.Format(CultureInfo.InvariantCulture, "{0}k", config.AacBitrate));
            args.Add("-movflags");
            args.Add("+faststart");
        }

        args.Add(outputFile);

        await RunAsync(_mediaEncoder.EncoderPath, args, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConcatAsync(
        List<string> segmentFiles,
        string outputFile,
        int targetChannels,
        AudioFormat format,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (segmentFiles.Count == 1)
        {
            File.Copy(segmentFiles[0], outputFile, true);
            return;
        }

        var listFile = outputFile + ".txt";
        await File.WriteAllLinesAsync(
            listFile,
            segmentFiles.Select(f => $"file '{f.Replace("'", "'\\''", StringComparison.Ordinal)}'"),
            cancellationToken).ConfigureAwait(false);

        var concatArgs = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "info",
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            listFile,
            "-c",
            "copy"
        };

        if (format != AudioFormat.Mpeg3)
        {
            concatArgs.Add("-movflags");
            concatArgs.Add("+faststart");
        }

        concatArgs.Add(outputFile);

        try
        {
            await RunAsync(_mediaEncoder.EncoderPath, concatArgs, cancellationToken).ConfigureAwait(false);
            TryDelete(listFile);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Concat with stream copy failed, falling back to re-encode: {Message}", ex.Message);
            TryDelete(listFile);

            var filterParts = new List<string>();
            for (var i = 0; i < segmentFiles.Count; i++)
            {
                filterParts.Add(string.Format(CultureInfo.InvariantCulture, "[{0}:a]", i));
            }

            var inputArgs = new List<string>();
            foreach (var segment in segmentFiles)
            {
                inputArgs.Add("-i");
                inputArgs.Add(segment);
            }

            var filter = string.Join(
                string.Empty,
                string.Join(string.Empty, filterParts),
                string.Format(CultureInfo.InvariantCulture, "concat=n={0}:v=0:a=1[out]", segmentFiles.Count));

            var fallbackArgs = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "info",
                "-y"
            };
            fallbackArgs.AddRange(inputArgs);
            fallbackArgs.Add("-filter_complex");
            fallbackArgs.Add(filter);
            fallbackArgs.Add("-map");
            fallbackArgs.Add("[out]");
            fallbackArgs.Add("-ar");
            fallbackArgs.Add("48000");
            fallbackArgs.Add("-ac");
            fallbackArgs.Add(targetChannels.ToString(CultureInfo.InvariantCulture));

            if (format == AudioFormat.Mpeg3)
            {
                fallbackArgs.Add("-c:a");
                fallbackArgs.Add("libmp3lame");
                fallbackArgs.Add("-b:a");
                fallbackArgs.Add(string.Format(CultureInfo.InvariantCulture, "{0}k", config.Mp3Bitrate));
            }
            else
            {
                fallbackArgs.Add("-c:a");
                fallbackArgs.Add("aac");
                fallbackArgs.Add("-b:a");
                fallbackArgs.Add(string.Format(CultureInfo.InvariantCulture, "{0}k", config.AacBitrate));
                fallbackArgs.Add("-movflags");
                fallbackArgs.Add("+faststart");
            }

            fallbackArgs.Add(outputFile);

            await RunAsync(_mediaEncoder.EncoderPath, fallbackArgs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(string ffmpegPath, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var process = CreateProcess(ffmpegPath, args);
        var error = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                error.AppendLine(e.Data);
            }
        };

        var exitCode = 0;
        using (process)
        {
            try
            {
                _logger.LogInformation(
                    "Running ffmpeg: {Path} {Arguments}",
                    ffmpegPath,
                    ArgsToStringSafe(args));

                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            exitCode = process.ExitCode;
        }

        if (exitCode != 0)
        {
            _logger.LogWarning(
                "ffmpeg exited with code {ExitCode}: {Log}",
                exitCode,
                error.ToString());

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "ffmpeg exited with code {0}: {1}",
                    exitCode,
                    error.ToString()));
        }
    }

    private static Process CreateProcess(string ffmpegPath, IReadOnlyList<string> args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true
            }
        };

        foreach (var argument in args)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    internal static string BuildFilterGraph(IReadOnlyList<(double Start, double End)> removedIntervals)
    {
        var terms = new List<string>();
        foreach (var interval in removedIntervals)
        {
            if (double.IsInfinity(interval.End))
            {
                terms.Add(string.Format(CultureInfo.InvariantCulture, "gte(t\\,{0:0.###})", interval.Start));
            }
            else
            {
                terms.Add(string.Format(CultureInfo.InvariantCulture, "between(t\\,{0:0.###}\\,{1:0.###})", interval.Start, interval.End));
            }
        }

        var expression = string.Join("+", terms);
        return string.Format(CultureInfo.InvariantCulture, "aselect=not({0}),asetpts=N/SR/TB", expression);
    }

    internal static List<(double Start, double End)> MergeIntervals(IEnumerable<(double Start, double End)> intervals)
    {
        var sorted = intervals
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

        var merged = new List<(double Start, double End)>();
        foreach (var interval in sorted)
        {
            if (merged.Count == 0)
            {
                merged.Add(interval);
                continue;
            }

            var last = merged[^1];
            if (interval.End <= last.End)
            {
                continue;
            }

            var end = double.IsInfinity(interval.End) ? double.PositiveInfinity : interval.End;
            if (interval.Start <= last.End || double.IsInfinity(last.End))
            {
                merged[^1] = (last.Start, end);
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    internal static int ComputeTargetChannels(int? sourceChannels, AudioFormat format, int maxChannels)
    {
        var channels = sourceChannels ?? 2;
        if (format == AudioFormat.Mpeg3)
        {
            return Math.Min(2, Math.Max(1, channels));
        }

        return Math.Clamp(maxChannels, 1, Math.Max(1, channels));
    }

    internal static string BuildTrackTitle(MediaStream stream)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(stream.Language))
        {
            parts.Add(stream.Language);
        }

        if (!string.IsNullOrEmpty(stream.Codec))
        {
            parts.Add(stream.Codec.ToUpperInvariant());
        }

        if (!string.IsNullOrEmpty(stream.ChannelLayout))
        {
            parts.Add(stream.ChannelLayout);
        }
        else if (stream.Channels.HasValue)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} ch", stream.Channels.Value));
        }

        if (stream.IsDefault)
        {
            parts.Add("Default");
        }

        if (!string.IsNullOrEmpty(stream.Title))
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} - {1}", stream.Title, string.Join(", ", parts));
        }

        return string.Join(", ", parts);
    }

    internal static MediaStream? ResolveAudioStream(IReadOnlyList<MediaStream> streams, int requestedIndex, string preferredLanguage)
    {
        var audioStreams = streams
            .Where(s => s.Type == MediaStreamType.Audio && !s.IsExternal)
            .OrderBy(s => s.Index)
            .ToList();

        if (audioStreams.Count == 0)
        {
            return null;
        }

        if (requestedIndex >= 0)
        {
            var match = audioStreams.FirstOrDefault(s => s.Index == requestedIndex);
            if (match is not null)
            {
                return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            var langMatch = audioStreams.FirstOrDefault(s => string.Equals(s.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase));
            if (langMatch is not null)
            {
                return langMatch;
            }
        }

        var defaultMatch = audioStreams.FirstOrDefault(s => s.IsDefault);
        return defaultMatch ?? audioStreams[0];
    }

    internal static int IndexOfStream(IEnumerable<MediaStream> streams, MediaStream stream)
    {
        var index = 0;
        foreach (var candidate in streams)
        {
            if (ReferenceEquals(candidate, stream))
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    private static string? GetMediaFilePath(BaseItem item)
    {
        var source = GetFirstMediaSource(item);
        return string.IsNullOrWhiteSpace(source?.Path) ? null : source!.Path;
    }

    internal static MediaSourceInfo? GetFirstMediaSource(BaseItem item)
    {
        var sources = item.GetMediaSources(false).ToList();
        if (sources.Count == 0)
        {
            return null;
        }

        var fileSource = sources.FirstOrDefault(s => s.Protocol == MediaProtocol.File && !string.IsNullOrWhiteSpace(s.Path));
        if (fileSource is not null)
        {
            return fileSource;
        }

        return sources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Path));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            // Best effort cleanup.
            _ = ex;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Process already gone.
        }
    }

    private static string ArgsToStringSafe(IReadOnlyList<string> args)
    {
        return string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));
    }
}
