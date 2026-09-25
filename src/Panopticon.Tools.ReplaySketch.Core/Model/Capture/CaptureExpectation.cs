using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// A success condition evaluated after the capture. Unmet expectations fail the run explicitly
/// rather than letting an idle capture look successful. Discriminated by the JSON <c>Type</c>
/// property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(CaptureProjectileObservedExpectation), "ProjectileObserved")]
[JsonDerivedType(typeof(CaptureActorDamagedExpectation), "ActorDamaged")]
[JsonDerivedType(typeof(CaptureActorDestroyedExpectation), "ActorDestroyed")]
[JsonDerivedType(typeof(CaptureConstructionProgressedExpectation), "ConstructionProgressed")]
[JsonDerivedType(typeof(CaptureConstructionCompletedExpectation), "ConstructionCompleted")]
[JsonDerivedType(typeof(CaptureMinDisplacementExpectation), "MinDisplacement")]
[JsonDerivedType(typeof(CaptureProjectileOrientationExpectation), "ProjectileOrientation")]
public abstract class CaptureExpectation
{
    /// <summary>Optional stable ID used in result records; empty derives from the expectation itself.</summary>
    public string? Id { get; set; } = null;

    public abstract string Describe();
}

/// <summary>At least one projectile object was observed in flight during the capture.</summary>
public sealed class CaptureProjectileObservedExpectation : CaptureExpectation
{
    public override string Describe() => "a projectile is observed in flight";
}

/// <summary>Observed projectiles keep their local +X axis aligned to sampled travel.</summary>
public sealed class CaptureProjectileOrientationExpectation : CaptureExpectation
{
    /// <summary>Optional projectile definition filter. Empty includes every projectile.</summary>
    public string? Definition { get; set; } = null;

    public float MinimumAlignmentDot { get; set; } = 0.95f;

    public int MinimumSamples { get; set; } = 1;

    public bool RequireAscending { get; set; }

    public bool RequireDescending { get; set; }

    public override string Describe() =>
        $"{Definition ?? "projectiles"} align to travel at dot >= {MinimumAlignmentDot:0.###}";
}

/// <summary>The actor's health dropped below its post-setup value at some point.</summary>
public sealed class CaptureActorDamagedExpectation : CaptureExpectation
{
    public string ActorId { get; set; } = "";

    public override string Describe() => $"{ActorId} takes damage";
}

/// <summary>The actor died (health zero or object destroyed) during the capture.</summary>
public sealed class CaptureActorDestroyedExpectation : CaptureExpectation
{
    public string ActorId { get; set; } = "";

    public override string Describe() => $"{ActorId} is destroyed";
}

/// <summary>The bound construction actor's build progress exceeded zero.</summary>
public sealed class CaptureConstructionProgressedExpectation : CaptureExpectation
{
    public string ActorId { get; set; } = "";

    public override string Describe() => $"{ActorId} construction progresses";
}

/// <summary>The bound construction actor finished building (not under construction, progress complete).</summary>
public sealed class CaptureConstructionCompletedExpectation : CaptureExpectation
{
    public string ActorId { get; set; } = "";

    public override string Describe() => $"{ActorId} construction completes";
}

/// <summary>The actor moved at least <see cref="MinDisplacement"/> world units from its position when first measured.</summary>
public sealed class CaptureMinDisplacementExpectation : CaptureExpectation
{
    public string ActorId { get; set; } = "";

    public float MinDisplacement { get; set; } = 0f;

    public override string Describe() => $"{ActorId} moves at least {MinDisplacement:0.#} world units";
}
