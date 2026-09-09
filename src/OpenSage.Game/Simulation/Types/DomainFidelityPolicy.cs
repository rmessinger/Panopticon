#nullable enable

namespace OpenSage.Simulation;

public sealed record DomainFidelityPolicy(DomainFidelityLevel Level)
{
    public static readonly DomainFidelityPolicy Identity = new(DomainFidelityLevel.Identity);
}
