#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-04: the timeline model over a run bundle, engine-free. Artifacts are authored with the
/// SageGame-only persistence seam, so no install or booted game is needed.
/// </summary>
public sealed class RunBundleTimelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rs-timeline-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SeekTo_IntermediateFrame_ReconstructsObjectsAndMoney()
    {
        var bundlePath = WriteBundle("seek", lastFrame: 30, snapshotInterval: 10);
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundlePath), SageGame.CncGenerals);

        Assert.Equal(0u, timeline.FirstFrame);
        Assert.Equal(30u, timeline.LastFrame);

        var state = timeline.SeekTo(17);
        Assert.Equal(17u, state.Frame);
        var state12 = timeline.SeekTo(12);
        Assert.Equal(12u, state12.Frame);

        // Frame 17: objects 1-3 alive, object 10 left at 15, object 4 not yet joined (30). The
        // %5 rename is inert at 17 (17 % 5 == 2) and active one delta later, so seek 15 for it.
        Assert.Equal(new[] { 1u, 2u, 3u }, state.Objects.Select(o => o.ObjectId).ToArray());
        Assert.Equal("Thing2", state.Objects.Single(o => o.ObjectId == 2).DefinitionName);
        Assert.Equal(983u, state.PlayerMoney[0]);
        Assert.Equal(2017u, state.PlayerMoney[1]);
        var state15 = timeline.SeekTo(15);
        Assert.Equal("Renamed", state15.Objects.Single(o => o.ObjectId == 2).DefinitionName);

        // Backward seek still matches the projection.
        var state7 = timeline.SeekTo(7);
        Assert.Equal(new[] { 1u, 2u, 3u, 10u }, state7.Objects.Select(o => o.ObjectId).ToArray());
        Assert.Equal("Thing2", state7.Objects.Single(o => o.ObjectId == 2).DefinitionName);
        Assert.Equal(993u, state7.PlayerMoney[0]);
    }

    [Fact]
    public void Series_ExtractionNamesMoneyAndDomainSeries()
    {
        var bundlePath = WriteBundle("series", lastFrame: 20, snapshotInterval: 10);
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundlePath), SageGame.CncGenerals);

        var names = timeline.Series.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Contains("money:USA", names);
        Assert.Contains("money:GLA", names);
        Assert.Contains("unitCount:infantry", names);

        var money = timeline.Series.Single(s => s.Name == "money:USA");
        Assert.Equal(21, money.Points.Count);
        Assert.Equal(1000d, money.Points[0].Value);
        Assert.Equal(980d, money.Points[20].Value);

        var infantry = timeline.Series.Single(s => s.Name == "unitCount:infantry");
        Assert.Equal(11, infantry.Points.Count);
        Assert.All(infantry.Points, p => Assert.Equal(4d, p.Value));
    }

    [Fact]
    public void EventsThrough_FiltersByFrame()
    {
        var bundlePath = WriteBundle("events", lastFrame: 12, snapshotInterval: 6);
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundlePath), SageGame.CncGenerals);

        timeline.SeekTo(4);
        var through4 = timeline.EventsThrough(timeline.CurrentFrame);
        Assert.Equal(new[] { 0u, 3u }, through4.Select(e => e.Frame).ToArray());

        timeline.SeekTo(11);
        var through11 = timeline.EventsThrough(timeline.CurrentFrame);
        Assert.Equal(new[] { 0u, 3u, 7u, 11u }, through11.Select(e => e.Frame).ToArray());
    }

    [Fact]
    public void TruncatedArtifact_PassesThroughDiagnostics()
    {
        var bundlePath = WriteBundle("torn", lastFrame: 12, snapshotInterval: 6);
        var statePath = Path.Combine(bundlePath, RunBundle.StateFileName);
        var bytes = File.ReadAllBytes(statePath);
        using (var stream = new FileStream(statePath, FileMode.Truncate, FileAccess.Write))
        {
            stream.Write(bytes, 0, bytes.Length - 4);
        }

        using var timeline = new RunBundleTimeline(RunBundle.Load(bundlePath), SageGame.CncGenerals);
        Assert.True(timeline.IsTruncated);
        Assert.Contains("offset", timeline.TruncationDiagnostic, StringComparison.Ordinal);
        Assert.Equal(11u, timeline.LastFrame);
        Assert.Equal(11u, timeline.SeekTo(11).Frame);
    }

    private string WriteBundle(string name, uint lastFrame, uint snapshotInterval)
    {
        return TimelineFixture.WriteBundle(Path.Combine(_root, name), lastFrame, snapshotInterval);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
