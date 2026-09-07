#nullable enable

using System;

namespace OpenSage.Logic.AI;

/// <summary>
/// The only place that maps an <see cref="AIDecisionPolicyType"/> identifier to an
/// <see cref="IAIDecisionPolicy"/> implementation. Callers hand over the identifier; nothing
/// else branches on policy type.
/// </summary>
public static class AIDecisionPolicyFactory
{
    public static IAIDecisionPolicy Create(AIDecisionPolicyType policyType, float[]? neuralNetworkWeights = null)
    {
        return policyType switch
        {
            AIDecisionPolicyType.RuleBased => new RuleBasedAIDecisionPolicy(),
            AIDecisionPolicyType.NeuralNetwork => new NeuralNetworkAIDecisionPolicy(
                neuralNetworkWeights ?? throw new ArgumentException(
                    $"The {nameof(AIDecisionPolicyType.NeuralNetwork)} AI decision policy requires a weight vector.",
                    nameof(neuralNetworkWeights))),
            _ => throw new ArgumentOutOfRangeException(nameof(policyType), policyType, "Unknown AI decision policy type."),
        };
    }
}
