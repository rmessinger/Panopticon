#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// Reference air commander (WI-M4-05). Missions rotate deterministically: the decision-tick
/// count divided into windows of <see cref="MissionWindowTicks"/> ticks, cycling Strike,
/// Patrol, Escort in that order, so every mission type recurs and a scaled run exercises all
/// three. Strike attacks the general's directive target when one exists, else the nearest
/// enemy attack-capable ground unit to the squad's centroid. Patrol flies a leg between the
/// first and last friendly ground squad centroids (alternating by decision-tick parity);
/// escort shadows the first friendly ground squad's centroid. All choices are first-by-order
/// and therefore deterministic.
/// </summary>
public sealed class RuleBasedAirCommanderDecisionPolicy : IAirCommanderDecisionPolicy
{
    internal const int MissionWindowTicks = 3;

    private static readonly AirMission[] Rotation = [AirMission.Strike, AirMission.Patrol, AirMission.Escort];

    public IReadOnlyList<AirSquadMission> DecideMissions(in AirCommanderContext context)
    {
        // Windows are tick-count aligned: ticks 1-3 Strike, 4-6 Patrol, 7-9 Escort, cycling.
        var window = (context.Context.DecisionTickCount - 1) / MissionWindowTicks;
        var mission = Rotation[window % Rotation.Length];
        var missions = new List<AirSquadMission>(context.AirSquads.Count);
        foreach (var squad in context.AirSquads)
        {
            missions.Add(new AirSquadMission(
                squad.Id,
                mission,
                StrikeTargetId(context),
                DestinationFor(mission, context, squad)));
        }

        return missions;
    }

    private static ObjectId? StrikeTargetId(in AirCommanderContext context) =>
        context.Directive is { Posture: DirectivePosture.Attack, TargetId: { } targetId }
            ? targetId
            : NearestEnemyGroundUnit(context);

    private static ObjectId? NearestEnemyGroundUnit(in AirCommanderContext context)
    {
        var centroid = context.AirSquads.Count > 0
            ? RuleBasedCommanderDecisionPolicy.Centroid(context.AirSquads[0])
            : Vector3.Zero;
        GameObject? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var enemy in context.Context.Owner.Enemies)
        {
            foreach (var unit in enemy.OwnedObjects)
            {
                if (unit.IsStructure || !unit.CanAttack)
                {
                    continue;
                }

                var distance = Vector3.DistanceSquared(centroid, unit.Translation);
                if (distance < nearestDistance)
                {
                    nearest = unit;
                    nearestDistance = distance;
                }
            }
        }

        return nearest?.Id;
    }

    private static Vector3? DestinationFor(AirMission mission, in AirCommanderContext context, Squad squad) =>
        mission switch
        {
            AirMission.Strike => StrikeDestination(context),
            AirMission.Patrol => PatrolDestination(context),
            AirMission.Escort => EscortDestination(context),
            _ => null,
        };

    private static Vector3? StrikeDestination(in AirCommanderContext context)
    {
        foreach (var enemy in context.Context.Owner.Enemies)
        {
            var target = enemy.OwnedObjects.FirstOrDefault(u => !u.IsStructure && u.CanAttack)
                ?? enemy.OwnedObjects.FirstOrDefault(u => u.IsStructure);
            if (target != null)
            {
                return target.Translation;
            }
        }

        return null;
    }

    // Patrol legs alternate between the first and last friendly ground squad centroid so a
    // parked squad that accepts consecutive patrol windows still changes destination.
    private static Vector3? PatrolDestination(in AirCommanderContext context)
    {
        if (context.FriendlyGroundSquads.Count == 0)
        {
            return null;
        }

        var first = context.FriendlyGroundSquads[0];
        var last = context.FriendlyGroundSquads[^1];
        return ((context.Context.DecisionTickCount - 1) / MissionWindowTicks) % 2 == 0
            ? first.Centroid
            : last.Centroid;
    }

    private static Vector3? EscortDestination(in AirCommanderContext context) =>
        context.FriendlyGroundSquads.Count > 0 ? context.FriendlyGroundSquads[0].Centroid : null;
}
