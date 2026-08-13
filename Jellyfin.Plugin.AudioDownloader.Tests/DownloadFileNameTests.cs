using System;
using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Dto;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class DownloadFileNameTests
{
    [Theory]
    [InlineData("My Show", 1, 3, "The Pilot", "My Show-S01E03-The Pilot")]
    [InlineData("My Show", 12, 2, "The Pilot", "My Show-S12E02-The Pilot")]
    [InlineData("My", 1, 2, "Show", "My-S01E02-Show")]
    public void Episode_WithSeasonAndEpisode_ReturnsShowSeasonEpisodeName(
        string seriesName,
        int season,
        int episodeNumber,
        string episodeName,
        string expected)
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), episodeName, season, episodeNumber).Object;
        item.SeriesName = seriesName;

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("My Show", "The Pilot", "My Show-The Pilot")]
    [InlineData("My Show", "Special", "My Show-Special")]
    public void Episode_WithoutSeasonOrEpisode_ReturnsShowEpisodeName(
        string seriesName,
        string episodeName,
        string expected)
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), episodeName, 1, 1).Object;
        item.SeriesName = seriesName;
        item.ParentIndexNumber = null;
        item.IndexNumber = null;

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Episode_WithMissingSeasonOnly_ReturnsShowEpisodeName()
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), "The Pilot", 1, 1).Object;
        item.SeriesName = "My Show";
        item.ParentIndexNumber = null;

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.Equal("My Show-The Pilot", result);
    }

    [Fact]
    public void Episode_WithMissingEpisodeOnly_ReturnsShowEpisodeName()
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), "The Pilot", 1, 1).Object;
        item.SeriesName = "My Show";
        item.IndexNumber = null;

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.Equal("My Show-The Pilot", result);
    }

    [Fact]
    public void Episode_SanitizesInvalidFileNameCharacters()
    {
        var item = TestHelpers.EpisodeItem(Guid.NewGuid(), "Pilot/Oops", 1, 1).Object;
        item.SeriesName = "My:Show";

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\0', result);
        Assert.EndsWith("S01E01-Pilot_Oops", result);
    }

    [Fact]
    public void Movie_ReturnsMovieTitle()
    {
        var item = TestHelpers.MovieItem(Guid.NewGuid(), "The Matrix").Object;

        var result = AudioProcessor.BuildDownloadFileName(item);

        Assert.Equal("The Matrix", result);
    }

    [Fact]
    public void Movie_WithoutName_FallsBackToAudioGuid()
    {
        var id = Guid.NewGuid();
        var mock = new Mock<Movie>();
        mock.Object.Id = id;
        TestHelpers.SetMediaSources(mock, Array.Empty<MediaSourceInfo>());

        var result = AudioProcessor.BuildDownloadFileName(mock.Object);

        Assert.Equal($"audio-{id:N}", result);
    }

    [Fact]
    public void Series_FallsBackToSeriesName()
    {
        var series = TestHelpers.SeriesItem(Guid.NewGuid());
        series.SetupGet(x => x.Name).Returns("Some Series");

        var result = AudioProcessor.BuildDownloadFileName(series.Object);

        Assert.Equal("Some Series", result);
    }
}