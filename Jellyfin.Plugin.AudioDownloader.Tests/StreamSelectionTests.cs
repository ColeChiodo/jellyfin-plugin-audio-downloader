using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class StreamSelectionTests
{
    private static readonly MediaStream EngDefault = TestHelpers.AudioStream(1, "eng", 2, isDefault: true);
    private static readonly MediaStream Deu = TestHelpers.AudioStream(2, "deu", 2);
    private static readonly MediaStream Spa = TestHelpers.AudioStream(3, "spa", 6);
    private static readonly MediaStream[] Streams = { EngDefault, Deu, Spa };

    [Fact]
    public void RequestedIndex_PicksExactStream()
    {
        var selected = AudioProcessor.ResolveAudioStream(Streams, 3, string.Empty);

        Assert.Same(Spa, selected);
    }

    [Fact]
    public void RequestedIndexMissing_FallsBackToPreferredLanguage()
    {
        var selected = AudioProcessor.ResolveAudioStream(Streams, 9, "deu");

        Assert.Same(Deu, selected);
    }

    [Fact]
    public void PreferredLanguage_IsCaseInsensitive()
    {
        var selected = AudioProcessor.ResolveAudioStream(Streams, -1, "ENG");

        Assert.Same(EngDefault, selected);
    }

    [Fact]
    public void NoIndexAndNoLanguage_PicksDefaultFlag()
    {
        var selected = AudioProcessor.ResolveAudioStream(Streams, -1, string.Empty);

        Assert.Same(EngDefault, selected);
    }

    [Fact]
    public void NoDefault_PicksFirstInIndexOrder()
    {
        var streams = new[] { TestHelpers.AudioStream(1, "eng", 2), TestHelpers.AudioStream(2, "deu", 2) };
        var selected = AudioProcessor.ResolveAudioStream(streams, -1, string.Empty);

        Assert.Same(streams[0], selected);
    }

    [Fact]
    public void NoAudioStreams_ReturnsNull()
    {
        var selected = AudioProcessor.ResolveAudioStream(new[] { TestHelpers.VideoStream(0) }, -1, string.Empty);

        Assert.Null(selected);
    }

    [Fact]
    public void ExternalAudioStreams_AreIgnored()
    {
        var streams = new[] { TestHelpers.AudioStream(1, "eng", 2, isExternal: true) };
        var selected = AudioProcessor.ResolveAudioStream(streams, -1, string.Empty);

        Assert.Null(selected);
    }

    [Fact]
    public void BuildTrackTitle_FormatsParts()
    {
        var stream = TestHelpers.AudioStream(1, "eng", 6, isDefault: true, codec: "truehd", layout: "5.1", title: "Main");

        var title = AudioProcessor.BuildTrackTitle(stream);

        Assert.Equal("Main - eng, TRUEHD, 5.1, Default", title);
    }

    [Fact]
    public void BuildTrackTitle_FallsBackToChannelCount()
    {
        var stream = TestHelpers.AudioStream(1, "eng", 2);

        var title = AudioProcessor.BuildTrackTitle(stream);

        Assert.Equal("eng, AC3, 2 ch", title);
    }

    [Fact]
    public void IndexOfStream_ReturnsPositionWithinFullStreamList()
    {
        var video = TestHelpers.VideoStream(0);
        var audio = TestHelpers.AudioStream(1, "eng", 2);
        var streams = new[] { video, audio };

        var index = AudioProcessor.IndexOfStream(streams, audio);

        // Position within the full stream list, not the filtered audio list.
        Assert.Equal(1, index);
    }

    [Fact]
    public void IndexOfStream_ReturnsZeroWhenNotFound()
    {
        var audio = TestHelpers.AudioStream(1, "eng", 2);
        var other = TestHelpers.AudioStream(2, "deu", 2);

        var index = AudioProcessor.IndexOfStream(new[] { audio }, other);

        Assert.Equal(0, index);
    }

    [Fact]
    public void IndexOfAudioStream_CountsOnlyAudioStreams()
    {
        var video = TestHelpers.VideoStream(0);
        var engSubs = new MediaStream
        {
            Type = MediaStreamType.Subtitle,
            Index = 1
        };
        var audio = TestHelpers.AudioStream(2, "eng", 2);
        var deuAudio = TestHelpers.AudioStream(3, "deu", 2);
        var streams = new[] { video, engSubs, audio, deuAudio };

        var audioOrdinal = AudioProcessor.IndexOfAudioStream(streams, deuAudio);

        Assert.Equal(1, audioOrdinal);
    }

    [Fact]
    public void IndexOfAudioStream_ReturnsZeroWhenNotFound()
    {
        var audio = TestHelpers.AudioStream(1, "eng", 2);
        var other = TestHelpers.AudioStream(2, "deu", 2);

        var index = AudioProcessor.IndexOfAudioStream(new[] { audio }, other);

        Assert.Equal(0, index);
    }
}