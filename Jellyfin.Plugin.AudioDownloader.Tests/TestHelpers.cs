using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Moq;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

internal static class TestHelpers
{
    public static Mock<Movie> MovieItem(Guid id, string name)
    {
        var mock = new Mock<Movie>();
        mock.Object.Id = id;
        mock.SetupGet(x => x.Name).Returns(name);
        SetMediaSources(mock, new List<MediaSourceInfo>());
        return mock;
    }

    public static Mock<Episode> EpisodeItem(Guid id, string name, int parentIndex, int index)
    {
        var mock = new Mock<Episode>();
        mock.Object.Id = id;
        mock.SetupGet(x => x.Name).Returns(name);
        mock.Object.ParentIndexNumber = parentIndex;
        mock.Object.IndexNumber = index;
        SetMediaSources(mock, new List<MediaSourceInfo>());
        return mock;
    }

    public static Mock<Season> SeasonItem(Guid id)
    {
        var mock = new Mock<Season>();
        mock.Object.Id = id;
        return mock;
    }

    public static Mock<Series> SeriesItem(Guid id)
    {
        var mock = new Mock<Series>();
        mock.Object.Id = id;
        return mock;
    }

    public static Mock<BaseItem> BaseItem(Guid id, string name)
    {
        var mock = new Mock<BaseItem>();
        mock.Object.Id = id;
        mock.SetupGet(x => x.Name).Returns(name);
        SetMediaSources(mock, new List<MediaSourceInfo>());
        return mock;
    }

    public static void SetMediaSources<T>(Mock<T> item, IReadOnlyList<MediaSourceInfo> sources)
        where T : BaseItem
    {
        item.Setup(x => x.GetMediaSources(It.IsAny<bool>())).Returns(sources);
    }

    public static MediaSourceInfo FileSource(params MediaStream[] streams)
    {
        return new MediaSourceInfo
        {
            Protocol = MediaProtocol.File,
            Path = "/media/sample.mkv",
            MediaStreams = new List<MediaStream>(streams)
        };
    }

    public static MediaSourceInfo Source(string path, MediaProtocol protocol, params MediaStream[] streams)
    {
        return new MediaSourceInfo
        {
            Protocol = protocol,
            Path = path,
            MediaStreams = new List<MediaStream>(streams)
        };
    }

    public static MediaStream AudioStream(
        int index,
        string language,
        int channels,
        bool isDefault = false,
        bool isExternal = false,
        string codec = "ac3",
        string? layout = null,
        string? title = null)
    {
        return new MediaStream
        {
            Type = MediaStreamType.Audio,
            Index = index,
            Language = language,
            Codec = codec,
            Channels = channels,
            ChannelLayout = layout,
            IsDefault = isDefault,
            IsExternal = isExternal,
            Title = title
        };
    }

    public static MediaStream VideoStream(int index, string codec = "h264")
    {
        return new MediaStream
        {
            Type = MediaStreamType.Video,
            Index = index,
            Codec = codec
        };
    }
}