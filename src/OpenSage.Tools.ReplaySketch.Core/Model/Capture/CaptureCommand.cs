using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// A scripted action executed before the simulation tick of its capture-relative logic frame,
/// in declaration order. Concrete types are discriminated by the JSON <c>Type</c> property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(CaptureAttackCommand), "Attack")]
[JsonDerivedType(typeof(CaptureConstructCommand), "Construct")]
[JsonDerivedType(typeof(CaptureApplyImpulseCommand), "ApplyImpulse")]
public abstract class CaptureCommand
{
    /// <summary>Capture-relative logic frame to execute on; 0 is the first captured tick.</summary>
    public int Frame { get; set; } = 0;

    public abstract string Describe();
}

/// <summary>
/// Commands the source actor's weapon directly (an AttackObject order would move the actor and
/// drift the framing). The weapon keeps its target until it dies.
/// </summary>
public sealed class CaptureAttackCommand : CaptureCommand
{
    public string SourceActorId { get; set; } = "";

    public string TargetActorId { get; set; } = "";

    public override string Describe() => $"Attack {SourceActorId} -> {TargetActorId}";
}

/// <summary>
/// Places a building through the real engine build order: the builder is verified to have a
/// builder AI, funds are verified, exactly that builder is selected, and
/// <c>Order.CreateBuildObject</c> is dispatched. The synchronously created building object is
/// bound to <see cref="ResultActorId"/> and tracked from then on.
/// </summary>
public sealed class CaptureConstructCommand : CaptureCommand
{
    public string BuilderActorId { get; set; } = "";

    public string BuildingDefinition { get; set; } = "";

    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>Building facing in degrees.</summary>
    public float FacingDegrees { get; set; } = 0f;

    /// <summary>Stable actor ID assigned to the created building. Must not collide with existing IDs.</summary>
    public string ResultActorId { get; set; } = "";

    public override string Describe() => $"Construct {BuildingDefinition} by {BuilderActorId} as {ResultActorId}";
}

/// <summary>
/// Applies a mass-independent velocity change to an actor with physics:
/// <c>ApplyForce(DeltaVelocity * Mass)</c>, so the resulting velocity delta is the configured
/// value regardless of the actor's mass.
/// </summary>
public sealed class CaptureApplyImpulseCommand : CaptureCommand
{
    public string ActorId { get; set; } = "";

    public float DeltaVelocityX { get; set; }

    public float DeltaVelocityY { get; set; }

    public float DeltaVelocityZ { get; set; }

    public override string Describe() =>
        $"ApplyImpulse {ActorId} ({DeltaVelocityX:0.##}, {DeltaVelocityY:0.##}, {DeltaVelocityZ:0.##})";
}
