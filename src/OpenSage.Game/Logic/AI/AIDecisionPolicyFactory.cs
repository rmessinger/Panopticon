#nullable enable

using System;
using OpenSage.Logic.AI.Hierarchy;

namespace OpenSage.Logic.AI;

/// <summary>
/// The only place that maps policy configuration to an <see cref="IAIDecisionPolicy"/>
/// implementation. Callers hand over an <see cref="AIDecisionPolicyPayload"/>; nothing
/// else branches on policy type.
/// </summary>
public static class AIDecisionPolicyFactory
{
    public static IAIDecisionPolicy Create(AIDecisionPolicyPayload? payload)
    {
        payload ??= AIDecisionPolicyPayload.Default;

        return payload.Type switch
        {
            AIDecisionPolicyType.RuleBased => new RuleBasedAIDecisionPolicy(),
            AIDecisionPolicyType.NeuralNetwork => new NeuralNetworkAIDecisionPolicy(
                payload.NeuralNetworkWeights ?? throw new ArgumentException(
                    $"The {nameof(AIDecisionPolicyType.NeuralNetwork)} AI decision policy requires a weight vector.",
                    nameof(payload))),
            AIDecisionPolicyType.Hierarchical => new HierarchicalAIDecisionPolicy(
                new RuleBasedGeneralPolicy(),
                new RuleBasedCommanderDecisionPolicy(),
                new RuleBasedSoldierDecisionPolicy()),
            _ => throw new ArgumentOutOfRangeException(nameof(payload), payload.Type, "Unknown AI decision policy type."),
        };
    }

    public static IAIDecisionPolicy Create(AIDecisionPolicyType policyType, float[]? neuralNetworkWeights = null)
    {
        return Create(new AIDecisionPolicyPayload
        {
            Type = policyType,
            NeuralNetworkWeights = neuralNetworkWeights,
        });
    }
}
