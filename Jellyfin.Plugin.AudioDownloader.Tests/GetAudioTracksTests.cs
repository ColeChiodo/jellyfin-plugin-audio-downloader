using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class GetAudioTracksTests
{
    private static readonly AudioProcessor Processor = new(
        new Mock<MediaBrowser.Controller.MediaEncoding.IMediaEncoder>().Object,
        new Mock<MediaBrowser.Controller.MediaSegments.IMediaSegmentManager>().Object,
        new Mock<ILibraryManager>().Object,
        new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>().Object,
        NullLogger<AudioProcessor>.Instance);

    [Fact]
    public void ReturnsOnlyAudioTracks_InStreamOrder()
    {
        var item = TestHelpers.BaseItem(
            Guid.NewGuid(),
            "Item");
        TestHelpers.SetMediaSources(
            item,
            new[]
            {
                TestHelpers.FileSource(
                    TestHelpers.VideoStream(0),
                    TestHelpers.AudioStream(1, "eng", 2),
                    TestHelpers.AudioStream(2, "eng", 6, codec: "truehd", layout: "5.1")),
                TestHelpers.Source(
                    "/media/sub.srt",
                    MediaProtocol.File,
                    TestHelpers.AudioStream(5, "eng", 2, isExternal: true))
            });

        var tracks = Processor.GetAudioTracks(new[] { item.Object });

        Assert.Equal(new[] { 1, 2 }, tracks.Select(t => t.Index));
        Assert.Equal("eng", tracks[0].Language);
        Assert.Equal(2, tracks[0].Channels);
        Assert.DoesNotContain(tracks, t => t.Index == 5);
    }

    [Fact]
    public void DeduplicatesByStreamIndex_UnionsDefaultFlag()
    {
        var first = TestHelpers.BaseItem(Guid.NewGuid(), "A");
        TestHelpers.SetMediaSources(
            first,
            new[] { TestHelpers.FileSource(TestHelpers.AudioStream(1, "eng", 2)) });

        var second = TestHelpers.BaseItem(Guid.NewGuid(), "B");
        TestHelpers.SetMediaSources(
            second,
            new[] { TestHelpers.FileSource(TestHelpers.AudioStream(1, "eng", 2, isDefault: true)) });

        var tracks = Processor.GetAudioTracks(new[] { first.Object, second.Object });

        var track = Assert.Single(tracks);
        Assert.Equal(1, track.Index);
        Assert.True(track.IsDefault);
    }

    [Fact]
    public void SkipsItemsWithoutPlayableSource()
    {
        var withoutSource = TestHelpers.BaseItem(Guid.NewGuid(), "NoSource");
        TestHelpers.SetMediaSources(withoutSource, new List<MediaSourceInfo>());

        var withSource = TestHelpers.BaseItem(Guid.NewGuid(), "WithSource");
        TestHelpers.SetMediaSources(
            withSource,
            new[] { TestHelpers.FileSource(TestHelpers.AudioStream(1, "deu", 2)) });

        var tracks = Processor.GetAudioTracks(new[] { withoutSource.Object, withSource.Object });

        var track = Assert.Single(tracks);
        Assert.Equal("deu", track.Language);
    }

    [Fact]
    public void PrunesAudioStreamsThatAreExternal()
    {
        var item = TestHelpers.BaseItem(Guid.NewGuid(), "Item");
        TestHelpers.SetMediaSources(
            item,
            new[]
            {
                TestHelpers.FileSource(
                    TestHelpers.AudioStream(1, "eng", 2),
                    TestHelpers.AudioStream(2, "eng", 2, isExternal: true))
            });

        var tracks = Processor.GetAudioTracks(new[] { item.Object });

        var track = Assert.Single(tracks);
        Assert.Equal(1, track.Index);
    }

    [Fact]
    public void PrefersFileProtocolSource_WhenPresentWithPath()
    {
        var item = TestHelpers.BaseItem(Guid.NewGuid(), "Item");
        TestHelpers.SetMediaSources(
            item,
            new[]
            {
                TestHelpers.Source("/http/media", MediaProtocol.Http, TestHelpers.AudioStream(3, "spa", 2)),
                TestHelpers.FileSource(TestHelpers.AudioStream(1, "eng", 2))
            });

        var tracks = Processor.GetAudioTracks(new[] { item.Object });

        var track = Assert.Single(tracks);
        Assert.Equal(1, track.Index);
    }

    [Fact]
    public void ReturnsEmpty_WhenNoUsableStreams()
    {
        var item = TestHelpers.BaseItem(Guid.NewGuid(), "Item");
        TestHelpers.SetMediaSources(
            item,
            new[] { TestHelpers.FileSource(TestHelpers.VideoStream(0)) });

        var tracks = Processor.GetAudioTracks(new[] { item.Object });

        Assert.Empty(tracks);
    }
}