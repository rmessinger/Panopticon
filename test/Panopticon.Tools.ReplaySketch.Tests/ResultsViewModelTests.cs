using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class ResultsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-viewmodel-{Guid.NewGuid():N}");

    [Fact]
    public async Task Load_PopulatesTimelineSeriesAndEvents()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "bundle"), lastFrame: 30, snapshotInterval: 10);
        var viewModel = NewViewModel(bundleDir);

        viewModel.BundlePath = Path.Combine(bundleDir, "run.json");
        await viewModel.LoadCommand.ExecuteAsync();

        Assert.True(viewModel.HasTimeline);
        Assert.NotNull(viewModel.Timeline);
        Assert.Equal(0u, viewModel.SelectedFrame);
        Assert.Equal(30u, viewModel.Timeline!.LastFrame);
        Assert.Contains(viewModel.Series, s => s.Name == "money:USA");
        Assert.Contains(viewModel.Series, s => s.Name == "unitCount:infantry");
        Assert.All(viewModel.EventsThroughFrame, e => Assert.True(e.Frame <= 0u + 1u));
    }

    [Fact]
    public async Task ScrubTo_UpdatesFrameAndEventWindow()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "bundle"), lastFrame: 30, snapshotInterval: 10);
        var viewModel = NewViewModel(bundleDir);
        viewModel.BundlePath = Path.Combine(bundleDir, "run.json");
        await viewModel.LoadCommand.ExecuteAsync();

        viewModel.ScrubTo(11);

        Assert.Equal(11u, viewModel.SelectedFrame);
        var frames = viewModel.EventsThroughFrame.Select(e => e.Frame).ToArray();
        Assert.All(frames, frame => Assert.True(frame <= 11u));
        Assert.Contains(11u, frames);
    }

    [Fact]
    public async Task BadPath_ReportsLoadFailure()
    {
        var viewModel = NewViewModel(Path.Combine(_root, "missing"));
        viewModel.BundlePath = Path.Combine(_root, "missing", "run.json");
        await viewModel.LoadCommand.ExecuteAsync();

        Assert.False(viewModel.HasTimeline);
        Assert.Null(viewModel.Timeline);
        Assert.StartsWith("Load failed:", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browse_PicksPathAndLoads()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "bundle"), lastFrame: 20, snapshotInterval: 10);
        var picked = Path.Combine(bundleDir, "run.json");
        var viewModel = new ResultsViewModel(new StubSelector(picked));

        await viewModel.BrowseCommand.ExecuteAsync();

        Assert.True(viewModel.HasTimeline);
        Assert.Equal(picked, viewModel.BundlePath);
    }

    private ResultsViewModel NewViewModel(string _)
    {
        return new ResultsViewModel(new StubSelector(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class StubSelector(string? path) : IBundleFileSelector
    {
        public Task<string?> SelectBundleAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(path);
        }
    }
}
