using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioDownloader.Configuration;
using Jellyfin.Plugin.AudioDownloader.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AudioDownloader.Tests;

public class BuildAudioFileTests
{
    private static readonly PluginConfiguration Config = new();

    private static AudioProcessor CreateProcessor()
    {
        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(x => x.TempDirectory).Returns(System.IO.Path.GetTempPath());

        return new AudioProcessor(
            new Mock<MediaBrowser.Controller.MediaEncoding.IMediaEncoder>().Object,
            new Mock<MediaBrowser.Controller.MediaSegments.IMediaSegmentManager>().Object,
            new Mock<ILibraryManager>().Object,
            applicationPaths.Object,
            NullLogger<AudioProcessor>.Instance);
    }

    [Fact]
    public async Task EmptyItemScope_Throws()
    {
        var processor = CreateProcessor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.BuildAudioFileAsync(
                Array.Empty<BaseItem>(),
                -1,
                AudioFormat.M4A,
                Config,
                CancellationToken.None));
    }

    [Fact]
    public async Task ItemsWithoutUsableAudio_Throws()
    {
        var processor = CreateProcessor();

        var item = TestHelpers.BaseItem(Guid.NewGuid(), "NoAudio");
        TestHelpers.SetMediaSources(
            item,
            new[] { TestHelpers.FileSource(TestHelpers.VideoStream(0)) });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.BuildAudioFileAsync(
                new[] { item.Object },
                -1,
                AudioFormat.M4A,
                Config,
                CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeProcessing()
    {
        var processor = CreateProcessor();

        var item = TestHelpers.BaseItem(Guid.NewGuid(), "Item");
        TestHelpers.SetMediaSources(
            item,
            new[] { TestHelpers.FileSource(TestHelpers.AudioStream(1, "eng", 2)) });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.BuildAudioFileAsync(
                new[] { item.Object },
                -1,
                AudioFormat.M4A,
                Config,
                cts.Token));
    }
}