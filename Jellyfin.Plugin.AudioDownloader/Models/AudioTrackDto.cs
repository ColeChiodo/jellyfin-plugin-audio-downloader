namespace Jellyfin.Plugin.AudioDownloader.Models;

/// <summary>
/// Describes an audio track available on an item, used to populate the client side chooser.
/// </summary>
public sealed class AudioTrackDto
{
    /// <summary>
    /// Gets or sets the stream index of the track within the item's container.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the ISO language code of the track, if known.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the codec name, e.g. "ac3".
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets the number of audio channels, if known.
    /// </summary>
    public int? Channels { get; set; }

    /// <summary>
    /// Gets or sets the human readable channel layout, e.g. "5.1".
    /// </summary>
    public string? ChannelLayout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the container default track.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a display title suitable for a picker, e.g. "English (eng) - AC3 - 6 ch (Default)".
    /// </summary>
    public string? DisplayTitle { get; set; }
}
