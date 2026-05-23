using System;
using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FixedTiming), "fixed")]
[JsonDerivedType(typeof(RandomTiming), "random")]
[JsonDerivedType(typeof(AfterActionCompletedTiming), "afterActionCompleted")]
public abstract record TimingConfig
{
    public abstract uint Resolve(Random rng);
}

public record FixedTiming(uint Frames) : TimingConfig
{
    public override uint Resolve(Random rng) => Frames;
}

public record RandomTiming(uint MinFrames, uint MaxFrames) : TimingConfig
{
    public override uint Resolve(Random rng) =>
        (uint)rng.Next((int)MinFrames, (int)MaxFrames + 1);
}

/// <summary>
/// Timing kind that defers an action until a specific <see cref="ActionType"/> has completed
/// at runtime, rather than at a pre-baked frame. The action is not written to the replay file;
/// instead it is injected by the test harness when the trigger event fires.
/// </summary>
public record AfterActionCompletedTiming(ActionType TriggerAction, uint ExtraFrames = 0) : TimingConfig
{
    /// <summary>
    /// Not used for replay export — deferred actions are injected at runtime.
    /// Returns 0 so the cumulative-frame cursor is not advanced.
    /// </summary>
    public override uint Resolve(Random rng) => 0;
}
