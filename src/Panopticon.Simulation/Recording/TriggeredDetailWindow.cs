#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// One T2 capture: the frame the trigger fired at (or the marked window bounds) and the bounded
/// per-frame delta window captured from the ring buffer.
/// </summary>
public sealed class TriggeredDetailWindow
{
    public bool Marked { get; init; }
    public uint TriggeredAtFrame { get; init; }
    public List<WorldStateDelta> Frames { get; } = new();
    public IReadOnlyList<string>? DivergedSubtrees { get; init; }

    /// <summary>WI-A5-21: why this window exists ("divergence" or "marked").</summary>
    public string Reason { get; init; } = "divergence";

    /// <summary>WI-A5-21: the budgets this window was captured under, echoed into the manifest.</summary>
    public int RingDepth { get; init; }
    public int MaxWindows { get; init; }
    public uint CooldownFrames { get; init; }

    /// <summary>WI-A5-21: region keys the frames were filtered to, or null when unfiltered.</summary>
    public IReadOnlyList<string>? RegionKeys { get; init; }

    /// <summary>WI-A5-21: distinct entity ids appearing across the captured frames.</summary>
    public int CapturedEntityCount { get; init; }

    public uint StartFrame => Frames.Count > 0 ? Frames[0].Frame : TriggeredAtFrame;
    public uint EndFrame => Frames.Count > 0 ? Frames[^1].Frame : TriggeredAtFrame;
}
