#nullable enable

using System.Numerics;

namespace OpenSage.Simulation;

/// <summary>
/// One comparable entity observation, the unit all recording tiers consume (A4-1).
/// <see cref="ScalarA"/>/<see cref="ScalarB"/> are the two domain-quantity slots the
/// recording tolerances quantize (RTS: health and max health; transport: edge progress and
/// zero). Two slots, not a vector, keeps the persisted <c>WorldObjectState</c> artifact
/// format unchanged.
/// </summary>
public readonly record struct DomainEntitySummary(
    uint EntityId,
    string KindName,
    int OwnerIndex,
    Vector3 Position,
    float ScalarA,
    float ScalarB);
