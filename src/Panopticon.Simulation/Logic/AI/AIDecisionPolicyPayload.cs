#nullable enable

namespace Panopticon.Logic.AI;

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

    /// <summary>Per-rank weight vectors for the Hierarchical type; null selects the
    /// rule-based reference ranks. Lengths are validated at the factory boundary
    /// (soldier 75, commander 156, general 116).</summary>
    public HierarchicalRankWeights? HierarchicalRankWeights { get; init; }

    public static AIDecisionPolicyPayload Default { get; } = new();
}

/// <summary>The three per-rank genomes of a hierarchical neural policy
/// (hierarchical-neural-network-policies.md §5). Treated as read-only; the factory clones.</summary>
public sealed record HierarchicalRankWeights(
    float[]? Soldier = null,
    float[]? Commander = null,
    float[]? General = null);
