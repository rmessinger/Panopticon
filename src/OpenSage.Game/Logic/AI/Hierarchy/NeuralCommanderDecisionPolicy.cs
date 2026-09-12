#nullable enable

using System;
using System.Collections.Generic;
using OpenSage.Logic.Object;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// The commander-rank network (A4-3e): 14-8-4 tanh/argmax over the documented squad-level
/// feature vector, decoding to a uniform goal template applied to every live member in
/// stable id order. Genome length 156. Contract: hierarchical-neural-network-policies.md §2.
/// </summary>
public sealed class NeuralCommanderDecisionPolicy : ICommanderDecisionPolicy
{
    public const int InputSize = 14;
    public const int HiddenSize = 8;
    public const int OutputSize = 4;
    public const int WeightCount = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;

    private const int MemberSlotCount = SquadHierarchy.MaxSquadSize;

    private readonly NeuralRankNetwork _network;

    public NeuralCommanderDecisionPolicy(float[]? weights)
    {
        _network = new NeuralRankNetwork(weights, InputSize, HiddenSize, OutputSize);
    }

    public IReadOnlyDictionary<uint, SoldierGoal> DecideGoals(in CommanderDecisionContext context)
    {
        if (context.Squad.Members.Count > SquadHierarchy.MaxSquadSize)
        {
            throw new InvalidOperationException(
                $"Squad {context.Squad.Id} has {context.Squad.Members.Count} members; the commander contract is bounded by {SquadHierarchy.MaxSquadSize}.");
        }

        Span<float> features = stackalloc float[InputSize];
        Encode(context, features);

        var goal = GoalTemplate(_network.Decide(features), context);
        var goals = new Dictionary<uint, SoldierGoal>(context.Squad.Members.Count);
        foreach (var member in context.Squad.Members)
        {
            goals[member.Id.Index] = goal;
        }

        return goals;
    }

    internal void Encode(in CommanderDecisionContext context, Span<float> features)
    {
        var directive = context.Directive;
        features[0] = directive is null ? 0.5f : (float)directive.Posture / 2f;
        features[1] = directive?.TargetId is { IsValid: true } ? 1f : 0f;
        features[2] = MathF.Min(context.Squad.Members.Count / (float)SquadHierarchy.MaxSquadSize, 1f);
        features[3] = AverageHealth(context.Squad);
        features[4] = Spread(context.Squad);

        for (var i = 0; i < MemberSlotCount; i++)
        {
            features[5 + i] = i < context.Squad.Members.Count ? HealthFraction(context.Squad.Members[i]) : 0f;
        }
    }

    private static SoldierGoal GoalTemplate(int action, in CommanderDecisionContext context)
    {
        var directive = context.Directive;
        var centroid = RuleBasedCommanderDecisionPolicy.Centroid(context.Squad);
        return action switch
        {
            1 => directive?.TargetId is { IsValid: true } target
                ? new SoldierGoal(TargetId: target, Stance: GoalStance.Advance)
                : directive?.TargetRegion is { } region
                    ? new SoldierGoal(Destination: region, Stance: GoalStance.Advance)
                    : new SoldierGoal(Destination: centroid, Stance: GoalStance.Hold),
            2 => directive?.TargetRegion is { } moveRegion
                ? new SoldierGoal(Destination: moveRegion, Stance: GoalStance.Advance)
                : new SoldierGoal(Destination: centroid, Stance: GoalStance.Hold),
            3 => new SoldierGoal(Destination: centroid, Stance: GoalStance.Retreat),
            _ => new SoldierGoal(Destination: centroid, Stance: GoalStance.Hold),
        };
    }

    private static float AverageHealth(Squad squad)
    {
        if (squad.Members.Count == 0)
        {
            return 0f;
        }

        var sum = 0f;
        foreach (var member in squad.Members)
        {
            sum += HealthFraction(member);
        }

        return sum / squad.Members.Count;
    }

    private static float HealthFraction(GameObject member)
    {
        var max = member.BodyModule?.MaxHealth ?? 0f;
        return max > 0f ? MathF.Min((member.BodyModule?.Health ?? 0f) / max, 1f) : 0f;
    }

    private static float Spread(Squad squad)
    {
        var centroid = RuleBasedCommanderDecisionPolicy.Centroid(squad);
        var max = 0f;
        foreach (var member in squad.Members)
        {
            var delta = member.Translation - centroid;
            max = MathF.Max(max, delta.Length());
        }

        return MathF.Min(max / 500f, 1f);
    }
}
