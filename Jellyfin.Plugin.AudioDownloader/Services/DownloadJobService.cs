using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Jellyfin.Plugin.AudioDownloader.Models;

namespace Jellyfin.Plugin.AudioDownloader.Services;

/// <summary>
/// Tracks asynchronous audio rendering jobs so the web client can poll progress
/// before downloading the finished file.
/// </summary>
public sealed class DownloadJobService : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadJobService"/> class.
    /// </summary>
    public DownloadJobService()
    {
        _cleanupTimer = new Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The states a job can be in.
    /// </summary>
    public enum JobState
    {
        /// <summary>
        /// The job is still rendering.
        /// </summary>
        Preparing,

        /// <summary>
        /// The rendered file is ready to be downloaded.
        /// </summary>
        Ready,

        /// <summary>
        /// The job failed.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Creates a new preparing job.
    /// </summary>
    /// <returns>The job identifier.</returns>
    public Guid Create()
    {
        var id = Guid.NewGuid();
        _jobs[id] = new Job(JobState.Preparing, "Preparing", 0, null, null, null, null, DateTime.UtcNow);
        return id;
    }

    /// <summary>
    /// Updates the progress of a preparing job.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="info">The progress information.</param>
    public void Update(Guid id, AudioProgressInfo info)
    {
        if (_jobs.TryGetValue(id, out var job) && job.State == JobState.Preparing)
        {
            _jobs[id] = job with { Phase = info.Phase, Fraction = info.Fraction };
        }
    }

    /// <summary>
    /// Marks a job as ready.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="outputPath">The finished audio file.</param>
    /// <param name="tempDirectory">The temporary directory holding the file.</param>
    /// <param name="downloadName">The base name to use for the downloaded file.</param>
    public void Complete(Guid id, string outputPath, string tempDirectory, string downloadName)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            _jobs[id] = job with
            {
                State = JobState.Ready,
                Phase = "Done",
                Fraction = 1,
                OutputPath = outputPath,
                TempDirectory = tempDirectory,
                DownloadName = downloadName
            };
        }
    }

    /// <summary>
    /// Marks a job as failed.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="error">The failure reason.</param>
    public void Fail(Guid id, string error)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            _jobs[id] = job with
            {
                State = JobState.Failed,
                Phase = "Failed",
                Error = error,
                TempDirectory = job.TempDirectory
            };
        }
    }

    /// <summary>
    /// Gets a progress snapshot of a job.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <returns>The progress snapshot, or <c>null</c> if the job does not exist.</returns>
    public AudioProgressDto? GetProgress(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return null;
        }

        return new AudioProgressDto(job.State.ToString(), job.Phase, job.Fraction, job.Error);
    }

    /// <summary>
    /// Claims a finished job, returning the file details to stream.
    /// </summary>
    /// <param name="id">The job identifier.</param>
    /// <param name="outputPath">The file to stream.</param>
    /// <param name="tempDirectory">The directory to remove once streaming finishes.</param>
    /// <param name="downloadName">The base name to use for the downloaded file.</param>
    /// <returns>Whether the job was ready and has been claimed.</returns>
    public bool TryClaimReadyJob(Guid id, out string? outputPath, out string? tempDirectory, out string? downloadName)
    {
        outputPath = null;
        tempDirectory = null;
        downloadName = null;

        if (!_jobs.TryGetValue(id, out var job) || job.State != JobState.Ready)
        {
            return false;
        }

        outputPath = job.OutputPath;
        tempDirectory = job.TempDirectory;
        downloadName = job.DownloadName;
        _jobs.TryRemove(id, out _);
        return true;
    }

    /// <summary>
    /// Counts how many jobs are still rendering, to bound concurrent encoding work.
    /// </summary>
    /// <returns>The number of preparing jobs.</returns>
    public int PreparingCount()
    {
        var count = 0;
        foreach (var job in _jobs.Values)
        {
            if (job.State == JobState.Preparing)
            {
                count++;
            }
        }

        return count;
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var pair in _jobs)
        {
            if (pair.Value.CreatedAt < cutoff)
            {
                _jobs.TryRemove(pair.Key, out var removed);
                TryDeleteDir(removed?.TempDirectory);
            }
        }
    }

    private static void TryDeleteDir(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _cleanupTimer.Dispose();
        foreach (var pair in _jobs)
        {
            TryDeleteDir(pair.Value.TempDirectory);
        }

        _jobs.Clear();
    }

    private sealed record Job(
        JobState State,
        string Phase,
        double Fraction,
        string? Error,
        string? OutputPath,
        string? TempDirectory,
        string? DownloadName,
        DateTime CreatedAt);
}
