#nullable enable

namespace OpenSage.Simulation;

/// <summary>
/// Domain fidelity policy hook (A4-1). v1 is the identity level everywhere; richer
/// aggregation policies arrive with B7 and extend adapters behind this attachment point
/// instead of a new seam.
/// </summary>
public enum DomainFidelityLevel
{
    /// <summary>Every entity simulated every frame at full detail (today's behavior).</summary>
    Identity = 0,

    /// <summary>Reserved for B7: domain-defined aggregation of entities or interactions.</summary>
    Aggregated = 1,
}

public sealed record DomainFidelityPolicy(DomainFidelityLevel Level)
{
    public static readonly DomainFidelityPolicy Identity = new(DomainFidelityLevel.Identity);
}
