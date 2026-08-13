using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AudioDownloader.Services;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class IntervalTests
{
    [Fact]
    public void MergeIntervals_CombinesOverlappingIntervals()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (0.0, 10.0), (5.0, 15.0) });

        var merged = Assert.Single(intervals);
        Assert.Equal((0.0, 15.0), merged);
    }

    [Fact]
    public void MergeIntervals_CombinesAdjacentIntervals()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (0.0, 10.0), (10.0, 20.0) });

        var merged = Assert.Single(intervals);
        Assert.Equal((0.0, 20.0), merged);
    }

    [Fact]
    public void MergeIntervals_KeepsDistinctIntervals()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (0.0, 5.0), (10.0, 15.0) });

        Assert.Equal(new[] { (0.0, 5.0), (10.0, 15.0) }, intervals.ToArray());
    }

    [Fact]
    public void MergeIntervals_DropsContainedIntervals()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (0.0, 20.0), (5.0, 10.0) });

        var merged = Assert.Single(intervals);
        Assert.Equal((0.0, 20.0), merged);
    }

    [Fact]
    public void MergeIntervals_OpenEndedIntervalAbsorbsEverythingAfter()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (5.0, 10.0), (12.0, double.PositiveInfinity) });

        Assert.Equal(2, intervals.Count);
        Assert.Equal((5.0, 10.0), intervals[0]);
        Assert.True(double.IsInfinity(intervals[1].End));
        Assert.Equal(12.0, intervals[1].Start);
    }

    [Fact]
    public void MergeIntervals_OpenEndedIntervalConsumesFollowingInterval()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (0.0, double.PositiveInfinity), (5.0, 6.0) });

        var merged = Assert.Single(intervals);
        Assert.True(double.IsInfinity(merged.End));
    }

    [Fact]
    public void MergeIntervals_IgnoresInvalidIntervals()
    {
        var intervals = AudioProcessor.MergeIntervals(new[] { (5.0, 5.0), (10.0, 8.0), (1.0, 2.0) });

        var merged = Assert.Single(intervals);
        Assert.Equal((1.0, 2.0), merged);
    }

    [Fact]
    public void BuildFilterGraph_EmitsBetweenTerms()
    {
        var graph = AudioProcessor.BuildFilterGraph(new[] { (1.0, 2.0), (3.5, 4.25) });

        Assert.Equal("aselect='not(between(t,1,2)+between(t,3.5,4.25))',asetpts=N/SR/TB", graph);
    }

    [Fact]
    public void BuildFilterGraph_EmitsOpenEndedTerm()
    {
        var graph = AudioProcessor.BuildFilterGraph(new[] { (5.0, double.PositiveInfinity) });

        Assert.Equal("aselect='not(gte(t,5))',asetpts=N/SR/TB", graph);
    }

    [Fact]
    public void BuildFilterGraph_EmitsSingleTerm()
    {
        var graph = AudioProcessor.BuildFilterGraph(new[] { (0.5, 10.0) });

        Assert.Equal("aselect='not(between(t,0.5,10))',asetpts=N/SR/TB", graph);
    }

    [Theory]
    [InlineData("silence_start: 1.5", 1.5, null)]
    [InlineData("silence_start: 3.25  silence_end: 5", 3.25, 5.0)]
    [InlineData("silence_end: 8", null, null)]
    public void ParseSilenceIntervals_PairsStartAndEnd(string log, double? expectedStart, double? expectedEnd)
    {
        var intervals = AudioProcessor.ParseSilenceIntervals(log);

        if (expectedStart is null && expectedEnd is null && intervals.Count == 0)
        {
            return;
        }

        var interval = Assert.Single(intervals);
        if (expectedStart.HasValue)
        {
            Assert.Equal(expectedStart.Value, interval.Start);
        }

        if (expectedEnd.HasValue)
        {
            Assert.Equal(expectedEnd.Value, interval.End);
        }
    }

    [Fact]
    public void ParseSilenceIntervals_TreatsUnpairedTrailingRunAsOpenEnded()
    {
        var intervals = AudioProcessor.ParseSilenceIntervals(
            "silence_start: 10\nsilence_start: 20\nsilence_end: 12");

        Assert.Equal(2, intervals.Count);
        Assert.Equal((10.0, 12.0), intervals[0]);
        Assert.Equal(20.0, intervals[1].Start);
        Assert.True(double.IsInfinity(intervals[1].End));
    }

    [Fact]
    public void ParseSilenceIntervals_ParsesMultipleRunsCaseInsensitively()
    {
        var intervals = AudioProcessor.ParseSilenceIntervals(
            "silence_start: 1\nsilence_end: 2\nsilence_start: 3\nsilence_end: 4");

        Assert.Equal(2, intervals.Count);
        Assert.Equal((1.0, 2.0), intervals[0]);
        Assert.Equal((3.0, 4.0), intervals[1]);
    }

    [Fact]
    public void ParseSilenceIntervals_IgnoresGarbage()
    {
        var intervals = AudioProcessor.ParseSilenceIntervals("no silence here");

        Assert.Empty(intervals);
    }
}