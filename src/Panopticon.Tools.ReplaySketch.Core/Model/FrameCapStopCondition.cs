namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>Stops the run once the given logic-frame count completes.</summary>
public sealed record FrameCapStopCondition(uint MaxFrames) : StopCondition;
