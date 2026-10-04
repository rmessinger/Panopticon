#nullable enable

namespace Panopticon.Simulation;

/// <summary>
/// The A4-4 optional state projection seam: a domain that can record reconstructable,
/// multi-valued, versioned state beside the existing entity summaries. The host and recorder
/// detect the projection by type test; RTS and existing adapters stay untouched.
/// Design: docs/roadmap/deep-dives/a4-4-domain-state-projection.md.
/// </summary>
public interface IDomainStateProjection
{
    /// <summary>The declared, versioned schema of the state this projection captures.</summary>
    DomainStateSchema StateSchema { get; }

    /// <summary>Captures the domain's complete state at one frame, records ordinal by key.</summary>
    DomainStateFrame CaptureState(uint frame);
}
