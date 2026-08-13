using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AudioDownloader.Configuration;

/// <summary>
/// The output audio format of the download.
/// </summary>
public enum AudioFormat
{
    /// <summary>
    /// MP3 (libmp3lame).
    /// </summary>
    Mpeg3,

    /// <summary>
    /// AAC in an M4A container.
    /// </summary>
    M4A
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        SilenceDurationSeconds = 3.0;
        SilenceThresholdDb = -45;
        IncludeCommercials = true;
        IncludePreviews = true;
        IncludeRecaps = false;
        DefaultFormat = AudioFormat.M4A;
        PreferredAudioLanguage = string.Empty;
        Mp3Bitrate = 256;
        AacBitrate = 192;
        AdminOnly = false;
        IncludeIntros = true;
        IncludeOutros = true;
        MaxChannels = 2;
    }

    /// <summary>
    /// Gets or sets the minimum duration in seconds of silence that should be treated as dead air and removed.
    /// A value of zero disables dead air removal.
    /// </summary>
    public double SilenceDurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the audio level (in dB) below which audio is considered silence.
    /// </summary>
    public int SilenceThresholdDb { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected commercial segments should be removed.
    /// </summary>
    public bool IncludeCommercials { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected preview segments should be removed.
    /// </summary>
    public bool IncludePreviews { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected recap segments should be removed.
    /// </summary>
    public bool IncludeRecaps { get; set; }

    /// <summary>
    /// Gets or sets the default output audio format.
    /// </summary>
    public AudioFormat DefaultFormat { get; set; }

    /// <summary>
    /// Gets or sets the preferred audio track language (ISO 639-1/2 code) used to select a track when
    /// multiple are available. Empty selects the container default track.
    /// </summary>
    public string PreferredAudioLanguage { get; set; }

    /// <summary>
    /// Gets or sets the MP3 output bitrate in kbps.
    /// </summary>
    public int Mp3Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the AAC output bitrate in kbps.
    /// </summary>
    public int AacBitrate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected intro segments should be removed.
    /// </summary>
    public bool IncludeIntros { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected outro segments should be removed.
    /// </summary>
    public bool IncludeOutros { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of output audio channels. Sources with more channels
    /// than this will be downmixed. MP3 output is always limited to two channels.
    /// </summary>
    public int MaxChannels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only administrators may use the download feature.
    /// </summary>
    public bool AdminOnly { get; set; }
}
