#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// Reference commander policy (A4-3c): aggregates its squad (member count, average health
/// fraction, centroid) and forwards the general's directive as each soldier's goal. An
/// attack directive with a target becomes a target goal for every member; an attack
/// directive with only a region becomes a move goal to the region; defend/regroup
/// directives become move goals to the squad's own centroid (hold position when empty).
/// </summary>
public sealed class RuleBasedCommanderDecisionPolicy : ICommanderDecisionPolicy
{
    public IReadOnlyDictionary<uint, SoldierGoal> DecideGoals(in CommanderDecisionContext context)
    {
        if (context.Squad.Members.Count > SquadHierarchy.MaxSquadSize)
        {
            throw new InvalidOperationException(
                $"Squad {context.Squad.Id} has {context.Squad.Members.Count} members; the commander contract is bounded by {SquadHierarchy.MaxSquadSize}.");
        }

        var goals = new Dictionary<uint, SoldierGoal>();
        foreach (var member in context.Squad.Members)
        {
            goals[member.Id.Index] = GoalFor(context, Centroid(context.Squad));
        }

        return goals;
    }

    private static SoldierGoal GoalFor(in CommanderDecisionContext context, Vector3 centroid)
    {
        var directive = context.Directive;
        if (directive is null)
        {
            return new SoldierGoal(Destination: centroid, Stance: GoalStance.Hold);
        }

        if (directive.Posture == DirectivePosture.Attack)
        {
            if (directive.TargetId is { IsValid: true } target)
            {
                return new SoldierGoal(TargetId: target, Stance: GoalStance.Advance);
            }

            if (directive.TargetRegion is { } region)
            {
                return new SoldierGoal(Destination: region, Stance: GoalStance.Advance);
            }
        }

        return new SoldierGoal(Destination: centroid, Stance: GoalStance.Hold);
    }

    internal static Vector3 Centroid(Squad squad)
    {
        if (squad.Members.Count == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (var member in squad.Members)
        {
            sum += member.Translation;
        }

        return sum / squad.Members.Count;
    }
}
