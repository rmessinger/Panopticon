#nullable enable

namespace OpenSage.Logic.AI;

/// <summary>
/// Typed AI decision policy configuration carried from player settings to
/// <see cref="AIDecisionPolicyFactory"/> (WI-A4-12). Null is equivalent to
/// <see cref="Default"/>: rule-based behavior with no payload. Payloads are immutable;
/// <see cref="NeuralNetworkWeights"/> is treated as read-only and the factory clones it
/// defensively.
/// </summary>
public sealed record AIDecisionPolicyPayload
{
    public AIDecisionPolicyType Type { get; init; } = AIDecisionPolicyType.RuleBased;

    /// <summary>Required iff <see cref="Type"/> is NeuralNetwork; must be exactly the
    /// policy's weight count (125 for the v1 network).</summary>
    public float[]? NeuralNetworkWeights { get; init; }

    public static AIDecisionPolicyPayload Default { get; } = new();
}
