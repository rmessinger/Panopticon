#nullable enable

using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>The mission an air squad flies (WI-M4-05).</summary>
public enum AirMission
{
    Strike = 0,
    Patrol = 1,
    Escort = 2,
}

/// <summary>
/// One air squad's mission assignment (WI-M4-05): data, not an order. A strike carries the
/// target reference when one exists; every mission carries a destination the squad can fly to
/// even without a specific target. A missing destination (null) means the mission cannot be
/// flown this tick and the squad holds.
/// </summary>
public sealed record AirSquadMission(
    uint SquadId,
    AirMission Mission,
    ObjectId? StrikeTargetId,
    Vector3? Destination);

/// <summary>
/// The air commander's input (WI-M4-05): the host's decision context, the air squads, the
/// friendly ground squads' summaries (patrol legs and escort destinations derive from them),
/// and the ground directive the general issued (strike prefers its target).
/// </summary>
public readonly record struct AirCommanderContext(
    AIDecisionContext Context,
    IReadOnlyList<Squad> AirSquads,
    IReadOnlyList<SquadSummary> FriendlyGroundSquads,
    CommanderDirective? Directive);

/// <summary>
/// The air rank of the command hierarchy (WI-M4-05): assigns one mission per air squad each
/// decision tick. Never mutates state and never emits orders itself; the host policy decides
/// whether a squad is ready to receive its mission (parked and rearmed).
/// </summary>
public interface IAirCommanderDecisionPolicy
{
    IReadOnlyList<AirSquadMission> DecideMissions(in AirCommanderContext context);
}
