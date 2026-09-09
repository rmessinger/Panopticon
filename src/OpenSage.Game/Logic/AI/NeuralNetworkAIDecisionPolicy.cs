#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic.Map;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI;

/// <summary>
/// Inference-only feed-forward network policy implementing <see cref="IAIDecisionPolicy"/> per
/// docs/roadmap/deep-dives/neural-network-ai-policy.md: encodes <see cref="AIDecisionContext"/>
/// into a fixed feature vector, runs a 9-8-5 tanh/softmax forward pass, and decodes the argmax
/// action into at most one order group. Weights are supplied at construction (the evolvable
/// genome); no training happens here.
/// </summary>
public sealed class NeuralNetworkAIDecisionPolicy : IAIDecisionPolicy
{
    public const int InputSize = 9;
    public const int HiddenSize = 8;
    public const int OutputSize = 5;

    public const int WeightCount = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;

    private const float CashNormalizationScale = 20000f;
    private const float AttackForceFeatureCap = 8f;
    private const float StructureFeatureCap = 20f;
    private const float HarvesterFeatureCap = 4f;
    private const float DozerFeatureCap = 2f;
    private const float IdleHarvesterFeatureCap = 4f;

    private readonly float[] _weights;

    /// <summary>Weight vector layout: W1 (per hidden unit), b1, W2 (per output unit), b2.</summary>
    public NeuralNetworkAIDecisionPolicy(float[]? weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length != WeightCount)
        {
            throw new ArgumentException(
                $"The neural-network AI decision policy requires exactly {WeightCount} weights "
                + $"({InputSize}x{HiddenSize} + {HiddenSize} + {HiddenSize}x{OutputSize} + {OutputSize}); "
                + $"got {weights.Length}.",
                nameof(weights));
        }

        _weights = (float[])weights.Clone();
    }

    public IReadOnlyList<Order> DecideNextOrders(AIDecisionContext context)
    {
        Span<float> features = stackalloc float[InputSize];
        EncodeState(context, features);

        Span<float> hidden = stackalloc float[HiddenSize];
        Span<float> logits = stackalloc float[OutputSize];
        ForwardPass(features, hidden, logits);

        return DecodeAction((NeuralNetworkAIAction)ArgMax(logits), context);
    }

    internal void EncodeState(AIDecisionContext context, Span<float> features)
    {
        var owner = context.Owner;

        features[0] = MathF.Min(
            MathF.Log(1f + owner.BankAccount.Money) / MathF.Log(1f + CashNormalizationScale),
            1f);
        features[1] = MathF.Min(CountAttackUnits(owner) / AttackForceFeatureCap, 1f);
        features[2] = MathF.Min(owner.OwnedObjects.Count(x => x.IsStructure) / StructureFeatureCap, 1f);
        features[3] = MathF.Min(CountKindOf(owner, ObjectKinds.Harvester) / HarvesterFeatureCap, 1f);
        features[4] = MathF.Min(CountKindOf(owner, ObjectKinds.Dozer) / DozerFeatureCap, 1f);
        features[5] = owner.Enemies.Any(
            enemy => enemy.OwnedObjects.Any(x => !x.IsStructure && x.CanAttack))
            ? 1f
            : 0f;
        features[6] = owner.Enemies.Any(
            enemy => enemy.OwnedObjects.Any(x => x.IsStructure))
            ? 1f
            : 0f;
        features[7] = owner.OwnedObjects.Any(
            x => x.IsStructure && x.Definition.KindOf.Get(ObjectKinds.SupplyGatheringCenter))
            ? 1f
            : 0f;
        features[8] = MathF.Min(CountIdleHarvesters(owner) / IdleHarvesterFeatureCap, 1f);
    }

    private static int CountAttackUnits(Player owner)
    {
        return owner.OwnedObjects.Count(
            x =>
                !x.IsStructure
                && x.CanAttack
                && !x.Definition.KindOf.Get(ObjectKinds.Harvester)
                && !x.Definition.KindOf.Get(ObjectKinds.Dozer));
    }

    private static int CountKindOf(Player owner, ObjectKinds kindOf)
    {
        return owner.OwnedObjects.Count(
            x => !x.IsStructure && x.Definition.KindOf.Get(kindOf));
    }

    private static int CountIdleHarvesters(Player owner)
    {
        return owner.OwnedObjects.Count(
            x =>
                !x.IsStructure
                && x.Definition.KindOf.Get(ObjectKinds.Harvester)
                && x.FindBehavior<SupplyAIUpdate>() is { CurrentSupplySource: null, CurrentSupplyTarget: null });
    }

    // Softmax is monotone in the logits, so the decision is the argmax over logits directly.
    internal void ForwardPass(ReadOnlySpan<float> features, Span<float> hidden, Span<float> logits)
    {
        for (var j = 0; j < HiddenSize; j++)
        {
            var sum = _weights[InputSize * HiddenSize + j];
            for (var i = 0; i < InputSize; i++)
            {
                sum += _weights[j * InputSize + i] * features[i];
            }

            hidden[j] = MathF.Tanh(sum);
        }

        var w2Offset = InputSize * HiddenSize + HiddenSize;
        for (var k = 0; k < OutputSize; k++)
        {
            var sum = _weights[w2Offset + HiddenSize * OutputSize + k];
            for (var j = 0; j < HiddenSize; j++)
            {
                sum += _weights[w2Offset + k * HiddenSize + j] * hidden[j];
            }

            logits[k] = sum;
        }
    }

    private static int ArgMax(ReadOnlySpan<float> logits)
    {
        var best = 0;
        for (var k = 1; k < logits.Length; k++)
        {
            if (logits[k] > logits[best])
            {
                best = k;
            }
        }

        return best;
    }

    private static IReadOnlyList<Order> DecodeAction(NeuralNetworkAIAction action, AIDecisionContext context)
    {
        return action switch
        {
            NeuralNetworkAIAction.BuildSupplyPriority => DecodeBuild(context, supplyOnly: true),
            NeuralNetworkAIAction.BuildNextStructure => DecodeBuild(context, supplyOnly: false),
            NeuralNetworkAIAction.ProduceCombatUnit =>
                RuleBasedAIDecisionPolicy.TryCreateUnitProductionOrder(context.Owner, context.OwnerPlayerIndex) is { } unitOrder
                    ? [unitOrder]
                    : [],
            NeuralNetworkAIAction.AttackNearestEnemy => DecodeAttack(context),
            _ => [],
        };
    }

    private static IReadOnlyList<Order> DecodeBuild(AIDecisionContext context, bool supplyOnly)
    {
        var owner = context.Owner;
        var buildList = RuleBasedAIDecisionPolicy.SelectBuildList(
            owner.Game.AssetStore.AIData.Current, owner.Side);
        if (buildList == null)
        {
            return [];
        }

        foreach (var structure in RuleBasedAIDecisionPolicy.OrderBuildListStructures(buildList.Structures))
        {
            if (supplyOnly && !structure.IsSupplyBuilding)
            {
                continue;
            }

            if (RuleBasedAIDecisionPolicy.HasStructureOrConstruction(owner, structure))
            {
                continue;
            }

            if (RuleBasedAIDecisionPolicy.TryCreateBuildOrder(context, structure) is { } buildOrder)
            {
                return [buildOrder];
            }
        }

        return [];
    }

    private static IReadOnlyList<Order> DecodeAttack(AIDecisionContext context)
    {
        var owner = context.Owner;
        var force = RuleBasedAIDecisionPolicy.TrySelectAttackForce(owner);
        if (force == null)
        {
            return [];
        }

        var target = RuleBasedAIDecisionPolicy.FindDefensiveTarget(owner)
            ?? RuleBasedAIDecisionPolicy.FindOffensiveTargetStructure(owner);
        if (target == null)
        {
            return [];
        }

        return RuleBasedAIDecisionPolicy.CreateAttackOrders(context.OwnerPlayerIndex, force, target);
    }
}
