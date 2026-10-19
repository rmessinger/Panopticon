#nullable enable

using System;
using Panopticon.Recording;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Scrub cursor over one recorded run's state artifact (WI-A5-08): a frame pointer plus
/// bounded-memory reconstruction through <see cref="WorldStateArtifactReader"/>. Seeks clamp to
/// the artifact's valid frame range; steps hold at the boundaries. The timeline/scrub UI
/// (WI-A5-04) builds on this without touching the artifact reader directly.
/// </summary>
public sealed class RunStateCursor
{
    private readonly WorldStateArtifactReader _reader;

    public RunStateCursor(WorldStateArtifactReader reader)
    {
        _reader = reader;
        CurrentFrame = reader.FirstFrame;
    }

    /// <summary>Frame the cursor currently points at.</summary>
    public uint CurrentFrame { get; private set; }

    /// <summary>Latest frame covered by the artifact's valid prefix.</summary>
    public uint LastFrame => _reader.LastFrame;

    /// <summary>Earliest reconstructable frame (frame zero when the bundle has the baseline snapshot).</summary>
    public uint FirstFrame => _reader.FirstFrame;

    /// <summary>Seeks to <paramref name="frame"/>, clamped to the valid range, returning the reconstructed state.</summary>
    public WorldStateSummary SeekTo(uint frame)
    {
        var clamped = Math.Clamp(frame, FirstFrame, LastFrame);
        CurrentFrame = clamped;
        return _reader.ReconstructStateAt(clamped);
    }

    /// <summary>Steps one frame forward, holding at <see cref="LastFrame"/>.</summary>
    public WorldStateSummary StepForward() => SeekTo(CurrentFrame + 1);

    /// <summary>Steps one frame backward, holding at <see cref="FirstFrame"/>.</summary>
    public WorldStateSummary StepBackward() => CurrentFrame == FirstFrame
        ? _reader.ReconstructStateAt(CurrentFrame)
        : SeekTo(CurrentFrame - 1);
}
