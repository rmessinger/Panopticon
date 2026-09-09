#nullable enable

namespace OpenSage.Simulation;

/// <summary>
/// One interaction between two entities at a topology key, submitted to the domain's
/// authority rule.
/// </summary>
public readonly record struct DomainInteraction(
    uint InitiatorId,
    uint CounterpartId,
    string TopologyKey);
