namespace Jellyfin.Plugin.AudioDownloader.Models;

/// <summary>
/// Output of the download preparation, referencing the final file on disk.
/// </summary>
public sealed class AudioDownloadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioDownloadResult"/> class.
    /// </summary>
    /// <param name="filePath">The path to the rendered audio file.</param>
    /// <param name="tempDirectory">The temporary directory that owns the file and should be deleted afterwards.</param>
    public AudioDownloadResult(string filePath, string tempDirectory)
    {
        FilePath = filePath;
        TempDirectory = tempDirectory;
    }

    /// <summary>
    /// Gets the full path to the rendered audio file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the temporary working directory containing the rendered file.
    /// </summary>
    public string TempDirectory { get; }
}
