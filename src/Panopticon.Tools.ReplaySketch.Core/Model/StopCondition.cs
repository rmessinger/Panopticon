using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// One scenario end criterion (WI-A5-03). Frame caps are enforced by the harness; victory ends
/// when the engine decides an outcome. Steady-state criteria are not yet expressible.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FrameCapStopCondition), "frameCap")]
[JsonDerivedType(typeof(VictoryStopCondition), "victory")]
public abstract record StopCondition;
