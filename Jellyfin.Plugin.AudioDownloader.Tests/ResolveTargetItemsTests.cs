using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class ResolveTargetItemsTests
{
    private static readonly Mock<ILibraryManager> Library = new();
    private static readonly AudioProcessor Processor = new(
        new Mock<MediaBrowser.Controller.MediaEncoding.IMediaEncoder>().Object,
        new Mock<MediaBrowser.Controller.MediaSegments.IMediaSegmentManager>().Object,
        Library.Object,
        new Mock<MediaBrowser.Common.Configuration.IApplicationPaths>().Object,
        NullLogger<AudioProcessor>.Instance);

    [Fact]
    public void Movie_ResolvesToItself()
    {
        var item = TestHelpers.MovieItem(Guid.NewGuid(), "Movie").Object;

        var result = Processor.ResolveTargetItems(item);

        var resolved = Assert.Single(result);
        Assert.Same(item, resolved);
    }

    [Fact]
    public void Episode_ResolvesToItself()
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), "Episode", 1, 2).Object;

        var result = Processor.ResolveTargetItems(item);

        var resolved = Assert.Single(result);
        Assert.Same(item, resolved);
    }

    [Fact]
    public void BaseItem_ResolvesToItself()
    {
        var item = TestHelpers.BaseItem(Guid.NewGuid(), "Other").Object;

        var result = Processor.ResolveTargetItems(item);

        var resolved = Assert.Single(result);
        Assert.Same(item, resolved);
    }

    [Fact]
    public void Season_ResolvesToOrderedEpisodes()
    {
        var scrambled = new List<BaseItem>
        {
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S01E02", 1, 2).Object,
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S01E03", 1, 3).Object,
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S01E01", 1, 1).Object
        };

        Library.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(scrambled);

        var season = TestHelpers.SeasonItem(Guid.NewGuid()).Object;
        var result = Processor.ResolveTargetItems(season);

        Assert.Equal(
            new[] { "S01E01", "S01E02", "S01E03" },
            result.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Series_ResolvesToEpisodes_GroupedBySeasonThenEpisode()
    {
        var scrambled = new List<BaseItem>
        {
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S02E01", 2, 1).Object,
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S01E02", 1, 2).Object,
            TestHelpers.EpisodeItem(Guid.NewGuid(), "S01E01", 1, 1).Object
        };

        Library.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(scrambled);

        var series = TestHelpers.SeriesItem(Guid.NewGuid()).Object;
        var result = Processor.ResolveTargetItems(series);

        Assert.Equal(
            new[] { "S01E01", "S01E02", "S02E01" },
            result.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Season_WithNoEpisodes_ReturnsEmpty()
    {
        Library.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem>());

        var season = TestHelpers.SeasonItem(Guid.NewGuid()).Object;
        var result = Processor.ResolveTargetItems(season);

        Assert.Empty(result);
    }
}