using Jellyfin.Plugin.AudioDownloader.Configuration;
using Jellyfin.Plugin.AudioDownloader.Services;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class ChannelTests
{
    [Fact]
    public void Mpeg3_IsCappedAtTwoChannels()
    {
        var channels = AudioProcessor.ComputeTargetChannels(8, AudioFormat.Mpeg3, 2);

        Assert.Equal(2, channels);
    }

    [Fact]
    public void Mpeg3_NeverDropsBelowOneChannel()
    {
        var channels = AudioProcessor.ComputeTargetChannels(1, AudioFormat.Mpeg3, 2);

        Assert.Equal(1, channels);
    }

    [Fact]
    public void Mpeg3_WithZeroChannelSource_DefaultsToTwo()
    {
        var channels = AudioProcessor.ComputeTargetChannels(0, AudioFormat.Mpeg3, 2);

        Assert.Equal(1, channels);
    }

    [Fact]
    public void M4A_RespectsConfiguredMaximum()
    {
        var channels = AudioProcessor.ComputeTargetChannels(8, AudioFormat.M4A, 2);

        Assert.Equal(2, channels);
    }

    [Fact]
    public void M4A_ClampsMaximumToSourceChannelCount()
    {
        var channels = AudioProcessor.ComputeTargetChannels(2, AudioFormat.M4A, 8);

        Assert.Equal(2, channels);
    }

    [Fact]
    public void M4A_NeverBelowOneChannel()
    {
        var channels = AudioProcessor.ComputeTargetChannels(2, AudioFormat.M4A, 0);

        Assert.Equal(1, channels);
    }

    [Fact]
    public void NullSourceChannels_AssumesTwo()
    {
        var m4a = AudioProcessor.ComputeTargetChannels(null, AudioFormat.M4A, 2);
        var mp3 = AudioProcessor.ComputeTargetChannels(null, AudioFormat.Mpeg3, 2);

        Assert.Equal(2, m4a);
        Assert.Equal(2, mp3);
    }
}