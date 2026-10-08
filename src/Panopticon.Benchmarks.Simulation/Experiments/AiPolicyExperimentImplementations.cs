using System;
using Panopticon.Logic.AI;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// The registered implementations of the AI decision policy seam
/// (<c>ai-policy</c>). Settings are <see cref="AIDecisionPolicyPayload"/> values; the runtime
/// selection surface stays <see cref="AIDecisionPolicyFactory"/>, so nothing here constructs
/// a policy directly.
/// </summary>
public static class AiPolicyExperimentImplementations
{
    public const string SeamKey = "ai-policy";

    public const string RuleBasedId = "rule-based";

    public const string NeuralId = "neural";

    public static ImplementationDescriptor RuleBased()
    {
        return new ImplementationDescriptor(
            SeamKey,
            RuleBasedId,
            "Rule-based skirmish AI policy (P2 reference)",
            "1",
            () => AIDecisionPolicyPayload.Default);
    }

    public static ImplementationDescriptor Neural()
    {
        return new ImplementationDescriptor(
            SeamKey,
            NeuralId,
            "Neural network AI policy (fixed zero genome)",
            "1",
            () => new AIDecisionPolicyPayload
            {
                Type = AIDecisionPolicyType.NeuralNetwork,
                NeuralNetworkWeights = new float[NeuralNetworkAIDecisionPolicy.WeightCount],
            });
    }
}
