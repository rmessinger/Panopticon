#nullable enable

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// The three rank populations of hierarchical co-evolution. The cycle order fixes the
/// alternating schedule: generation g advances <c>ranks[g % 3]</c>, starting with General.
/// </summary>
public enum NeuralRankKind
{
    General = 0,
    Commander = 1,
    Soldier = 2,
}

public static class HierarchicalEvolutionSchedule
{
    /// <summary>The rank advanced by <paramref name="generation"/> (WI-A4-29 contract).</summary>
    public static NeuralRankKind AdvancingRank(int generation) => (NeuralRankKind)(generation % 3);

    /// <summary>Per-rank genome length from the frozen contracts (A4-3e).</summary>
    public static int GenomeLengthOf(NeuralRankKind rank) => rank switch
    {
        NeuralRankKind.General => Panopticon.Logic.AI.Hierarchy.NeuralGeneralPolicy.WeightCount,
        NeuralRankKind.Commander => Panopticon.Logic.AI.Hierarchy.NeuralCommanderDecisionPolicy.WeightCount,
        NeuralRankKind.Soldier => Panopticon.Logic.AI.Hierarchy.NeuralSoldierDecisionPolicy.WeightCount,
        _ => throw new System.ArgumentOutOfRangeException(nameof(rank)),
    };
}
