#nullable enable

using System;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// The soldier-rank network (A4-3e): 8-6-3 tanh/argmax over the documented feature vector,
/// decoding to hold, attack-goal-target, or move-to-goal-destination orders. Genome length
/// 75. Contract: hierarchical-neural-network-policies.md §1.
/// </summary>
public sealed class NeuralSoldierDecisionPolicy : ISoldierDecisionPolicy
{
    public const int InputSize = 8;
    public const int HiddenSize = 6;
    public const int OutputSize = 3;
    public const int WeightCount = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;

    private readonly NeuralRankNetwork _network;

    public NeuralSoldierDecisionPolicy(float[]? weights)
    {
        _network = new NeuralRankNetwork(weights, InputSize, HiddenSize, OutputSize);
    }

    public Order? DecideOrder(in SoldierDecisionContext context)
    {
        Span<float> features = stackalloc float[InputSize];
        Encode(context, features);

        return _network.Decide(features) switch
        {
            1 => AttackGoalTarget(context),
            2 => MoveToGoalDestination(context),
            _ => null,
        };
    }

    internal void Encode(in SoldierDecisionContext context, Span<float> features)
    {
        var unit = context.Unit;
        var max = unit.BodyModule?.MaxHealth ?? 0f;
        features[0] = max > 0f ? MathF.Min((unit.BodyModule?.Health ?? 0f) / max, 1f) : 0f;
        features[1] = context.Goal?.TargetId is { IsValid: true } ? 1f : 0f;
        features[2] = context.Goal?.Destination is not null ? 1f : 0f;
        features[3] = context.Goal is { } goal ? (float)goal.Stance / 2f : 0f;

        var targetPosition = ResolveTargetPosition(context);
        if (targetPosition is { } target)
        {
            features[4] = MathF.Min(Distance(unit.Translation, target) / 1000f, 1f);
            features[6] = RelativeAxis(unit.Translation.X, target.X);
            features[7] = RelativeAxis(unit.Translation.Y, target.Y);
        }
        else
        {
            features[4] = 1f;
            features[6] = 0.5f;
            features[7] = 0.5f;
        }

        features[5] = context.Goal?.Destination is { } destination
            ? MathF.Min(Distance(unit.Translation, destination) / 1000f, 1f)
            : 1f;
    }

    private static Vector3? ResolveTargetPosition(in SoldierDecisionContext context)
    {
        if (context.Goal?.TargetId is not { IsValid: true } target)
        {
            return null;
        }

        // Ids that no longer resolve (dead or never-spawned targets) take the no-target
        // feature values; GetObjectById does not guard against stale ids.
        try
        {
            var targetObject = context.Unit.GameEngine.GameLogic.GetObjectById(target);
            return targetObject?.IsDestroyed == false ? targetObject.Translation : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static float Distance(Vector3 a, Vector3 b)
    {
        var delta = a - b;
        return delta.Length();
    }

    private static float RelativeAxis(float own, float target) =>
        (MathF.Max(-1f, MathF.Min(1f, (target - own) / 500f)) + 1f) / 2f;

    private static Order? AttackGoalTarget(in SoldierDecisionContext context) =>
        context.Goal?.TargetId is { IsValid: true } target
            ? Order.CreateAttackObject(context.OwnerPlayerIndex, target, force: true)
            : null;

    private static Order? MoveToGoalDestination(in SoldierDecisionContext context) =>
        context.Goal?.Destination is { } destination
            ? Order.CreateMoveOrder(context.OwnerPlayerIndex, destination)
            : null;
}
