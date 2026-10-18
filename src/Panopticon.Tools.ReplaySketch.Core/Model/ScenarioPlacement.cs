using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// One explicit initial placement (WI-A5-03): a definition spawned for a scenario player at a
/// declarative position before any player action runs. The position reuses the command
/// <see cref="PositionSpec"/> vocabulary so placements stay map-relative and replayable.
/// </summary>
public sealed class ScenarioPlacement
{
    /// <summary>Object definition name to spawn, e.g. <c>"AmericaTankPaladin"</c>.</summary>
    public string DefinitionName { get; set; } = string.Empty;

    /// <summary>Index into <see cref="ReplayScenario.Players"/> (0-based slot, not engine player id).</summary>
    public int PlayerIndex { get; set; }

    public required PositionSpec Position { get; set; }

    /// <summary>Spawn count for this placement entry; default 1.</summary>
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// One scenario end criterion (WI-A5-03). Frame caps are enforced by the harness; victory ends
/// when the engine decides an outcome. Steady-state criteria are not yet expressible.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FrameCapStopCondition), "frameCap")]
[JsonDerivedType(typeof(VictoryStopCondition), "victory")]
public abstract record StopCondition;

/// <summary>Stops the run once the given logic-frame count completes.</summary>
public sealed record FrameCapStopCondition(uint MaxFrames) : StopCondition;

/// <summary>Stops the run when the engine decides an outcome (P3).</summary>
public sealed record VictoryStopCondition : StopCondition;

/// <summary>
/// Recording options carried by a scenario (WI-A5-03). The sample interval configures the T0
/// metric recorder today; mode and snapshot interval are schema-complete and consumed by the
/// complete-bundle writer (WI-A5-09).
/// </summary>
public sealed class ScenarioRecordingOptions
{
    /// <summary>Tiered-recording mode; null keeps full mode.</summary>
    public Panopticon.Recording.RecordingMode? Mode { get; set; }

    /// <summary>T0 metric sample interval in logic frames; null keeps the recorder default.</summary>
    public uint? SampleInterval { get; set; }

    /// <summary>Snapshot interval in logic frames; null keeps the default of 300.</summary>
    public uint? SnapshotInterval { get; set; }
}
