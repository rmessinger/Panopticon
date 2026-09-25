#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// One squad's aggregated state as the general rank sees it (A4-3d): membership size,
/// average health fraction, and centroid.
/// </summary>
public readonly record struct SquadSummary(
    uint SquadId,
    int MemberCount,
    float AverageHealthFraction,
    Vector3 Centroid);

/// <summary>
/// The general rank's input: the host's decision context plus the per-squad summaries.
/// </summary>
public readonly record struct GeneralDecisionContext(
    AIDecisionContext Context,
    IReadOnlyList<SquadSummary> Squads);

/// <summary>
/// The top rank of the command hierarchy: one directive per squad, at the general's cadence
/// (every second decision tick). Never mutates state and never emits orders itself.
/// </summary>
public interface IGeneralDecisionPolicy
{
    IReadOnlyDictionary<uint, CommanderDirective> DecideDirectives(in GeneralDecisionContext context);
}
