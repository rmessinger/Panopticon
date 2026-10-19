using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Recording options carried by a scenario (WI-A5-03). The sample interval configures the T0
/// metric recorder today; mode and snapshot interval are schema-complete and consumed by the
/// complete-bundle writer (WI-A5-09). Detail options configure the T2 triggered capture
/// (WI-A5-21).
/// </summary>
public sealed class ScenarioRecordingOptions
{
    /// <summary>Tiered-recording mode; null keeps full mode.</summary>
    public Panopticon.Recording.RecordingMode? Mode { get; set; }

    /// <summary>T0 metric sample interval in logic frames; null keeps the recorder default.</summary>
    public uint? SampleInterval { get; set; }

    /// <summary>Snapshot interval in logic frames; null keeps the default of 300.</summary>
    public uint? SnapshotInterval { get; set; }

    /// <summary>T2 detail ring depth; null keeps the capture default.</summary>
    public int? DetailRingDepth { get; set; }

    /// <summary>T2 cap on captured windows; null keeps the capture default.</summary>
    public int? DetailMaxWindows { get; set; }

    /// <summary>T2 cooldown between trips in logic frames; null defaults to the ring depth.</summary>
    public uint? DetailCooldownFrames { get; set; }

    /// <summary>First frame of a pre-marked T2 detail window.</summary>
    public uint? DetailMarkedStartFrame { get; set; }

    /// <summary>Last frame of a pre-marked T2 detail window.</summary>
    public uint? DetailMarkedEndFrame { get; set; }

    /// <summary>Domain region keys T2 detail is filtered to; null keeps detail unfiltered.</summary>
    public List<string>? DetailRegionKeys { get; set; }

    /// <summary>WI-A5-21: translates the detail fields into capture options; null when unset.</summary>
    public Panopticon.Recording.TriggeredDetailCaptureOptions? ToDetailOptions()
    {
        var hasAny = DetailRingDepth is not null
            || DetailMaxWindows is not null
            || DetailCooldownFrames is not null
            || DetailMarkedStartFrame is not null
            || DetailMarkedEndFrame is not null
            || DetailRegionKeys is not null;
        return hasAny
            ? new Panopticon.Recording.TriggeredDetailCaptureOptions(
                DetailRingDepth,
                DetailMaxWindows,
                DetailCooldownFrames,
                DetailMarkedStartFrame,
                DetailMarkedEndFrame,
                DetailRegionKeys)
            : null;
    }
}
