#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-31: playback controller commands over a recorded timeline. A manual clock drives
/// ticks synchronously so tests assert exact frame state without timers.
/// </summary>
public sealed class ResultsPlaybackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-playback-{Guid.NewGuid():N}");

    private string WriteBundle(string name, uint lastFrame = 30, uint snapshotInterval = 10)
    {
        var dir = Path.Combine(_root, name);
        TimelineFixture.WriteBundle(dir, lastFrame, snapshotInterval);
        return dir;
    }

    [Fact]
    public void Play_AdvancesAndPausesExplicitlyAtLastFrame()
    {
        var bundle = WriteBundle("end");
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        var clock = new ManualClock();
        using var playback = new ResultsPlaybackController(timeline, clock);

        playback.Play();
        Assert.True(clock.Started);
        var frames = new List<uint>();
        for (var tick = 0; tick < 60; tick++)
        {
            clock.RaiseTick();
            frames.Add(playback.CurrentFrame);
        }

        Assert.False(playback.IsPlaying);
        Assert.Equal(playback.LastFrame, playback.CurrentFrame);
        Assert.All(frames, frame => Assert.InRange(frame, timeline.FirstFrame, timeline.LastFrame));
        Assert.Equal(timeline.LastFrame, frames.Max());
    }

    [Fact]
    public void StepCommands_MoveExactlyOneFrameAndPause()
    {
        var bundle = WriteBundle("step");
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        var clock = new ManualClock();
        using var playback = new ResultsPlaybackController(timeline, clock);

        playback.Play();
        playback.StepForward();
        Assert.False(playback.IsPlaying);
        Assert.Equal(1u, playback.CurrentFrame);

        playback.StepBackward();
        Assert.Equal(0u, playback.CurrentFrame);

        playback.StepBackward();
        Assert.Equal(0u, playback.CurrentFrame);
    }

    [Fact]
    public void FastForward_IsBoundedByLastFrame()
    {
        var bundle = WriteBundle("ff");
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        var clock = new ManualClock();
        using var playback = new ResultsPlaybackController(timeline, clock);

        playback.FastForward(5);
        Assert.Equal(5u, playback.CurrentFrame);
        Assert.False(playback.IsPlaying);

        playback.FastForward(30);
        Assert.Equal(playback.LastFrame, playback.CurrentFrame);
    }

    [Fact]
    public void Speed_OnlyAcceptsKnownValues()
    {
        var bundle = WriteBundle("speed");
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        using var playback = new ResultsPlaybackController(timeline, new ManualClock());

        playback.Speed = 2.0;
        Assert.Equal(2.0, playback.Speed);

        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Speed = 3.0);
    }

    [Fact]
    public void PlayAtEnd_RestartsFromFirstFrame()
    {
        var bundle = WriteBundle("restart");
        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        var clock = new ManualClock();
        using var playback = new ResultsPlaybackController(timeline, clock);

        playback.SeekTo(playback.LastFrame);
        Assert.True(playback.AtEnd);

        playback.Play();
        Assert.Equal(timeline.FirstFrame, playback.CurrentFrame);
        Assert.True(playback.IsPlaying);
    }

    [Fact]
    public void Truncated_PassthroughDiagnostic_AndBoundedStop()
    {
        var bundle = WriteBundle("torn");
        var statePath = Path.Combine(bundle, "state.bin");
        var bytes = File.ReadAllBytes(statePath);
        File.WriteAllBytes(statePath, bytes[..^4]);

        using var timeline = new RunBundleTimeline(RunBundle.Load(bundle)!,
            global::Panopticon.SageGame.CncGenerals);
        using var playback = new ResultsPlaybackController(timeline, new ManualClock());

        Assert.True(playback.IsTruncated);
        Assert.NotNull(playback.TruncationDiagnostic);

        playback.FastForward(uint.MaxValue);
        Assert.Equal(playback.LastFrame, playback.CurrentFrame);
        Assert.False(playback.IsPlaying);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ManualClock : IPlaybackClock
    {
        public bool Started { get; private set; }

        public event EventHandler? Tick;

        public void Start()
        {
            Started = true;
        }

        public void Stop()
        {
            Started = false;
        }

        public void RaiseTick()
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
