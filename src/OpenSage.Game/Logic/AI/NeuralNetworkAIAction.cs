namespace OpenSage.Logic.AI;

/// <summary>The five discrete outputs of the v1 network; see
/// docs/roadmap/deep-dives/neural-network-ai-policy.md §3.</summary>
internal enum NeuralNetworkAIAction
{
    None = 0,
    BuildSupplyPriority = 1,
    BuildNextStructure = 2,
    ProduceCombatUnit = 3,
    AttackNearestEnemy = 4,
}
