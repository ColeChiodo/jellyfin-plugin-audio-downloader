namespace Jellyfin.Plugin.AudioDownloader.Models;

/// <summary>
/// A progress snapshot returned to the web client for a download job.
/// </summary>
public sealed record AudioProgressDto(
    string State,
    string Phase,
    double Fraction,
    string? Error)
{
    /// <summary>
    /// Gets the job state: Preparing, Ready or Failed.
    /// </summary>
    public string State { get; init; } = State;

    /// <summary>
    /// Gets the current stage label.
    /// </summary>
    public string Phase { get; init; } = Phase;

    /// <summary>
    /// Gets the overall completion from 0 to 1.
    /// </summary>
    public double Fraction { get; init; } = Fraction;

    /// <summary>
    /// Gets the failure reason, if the job failed.
    /// </summary>
    public string? Error { get; init; } = Error;
}
