namespace Jellyfin.Plugin.AudioDownloader.Models;

/// <summary>
/// Describes the progress of an audio rendering job.
/// </summary>
public sealed record AudioProgressInfo(
    string Phase,
    double Fraction)
{
    /// <summary>
    /// Gets the human readable label of the current stage, eg. "Scanning" or "Encoding".
    /// </summary>
    public string Phase { get; init; } = Phase;

    /// <summary>
    /// Gets the overall completion from 0 to 1.
    /// </summary>
    public double Fraction { get; init; } = Fraction;
}
