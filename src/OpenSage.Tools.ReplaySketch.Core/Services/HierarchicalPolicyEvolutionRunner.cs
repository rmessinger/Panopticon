#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic.AI.Hierarchy;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// One complete hierarchy for a match: the per-rank weight vectors (the advancing rank's
/// genome plus the two partner genomes).
/// </summary>
public sealed record NeuralHierarchyGenomes(
    IReadOnlyList<float> General,
    IReadOnlyList<float> Commander,
    IReadOnlyList<float> Soldier);

/// <summary>
/// Evaluates one hierarchical match (WI-A4-29/30): the advancing rank's genome playing with
/// the fixed partner genomes, against the rule-based reference opponent, at the derived
/// match seed. Tests supply fakes; the headless adapter wraps the real match path.
/// </summary>
public interface IHierarchicalPolicyMatchEvaluator
{
    NeuralMatchEvaluation Evaluate(
        NeuralRankKind advancingRank,
        int genomeIndex,
        IReadOnlyList<float> genome,
        IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> partners,
        uint matchSeed);
}

/// <summary>One hierarchical generation's outcome: the stamped report and the updated rank
/// populations (only the advancing rank's population changes).</summary>
public sealed record HierarchicalGenerationResult(
    NeuralGenerationReport Report,
    NeuralRankKind AdvancedRank,
    IReadOnlyDictionary<NeuralRankKind, NeuralPopulation> Populations);

/// <summary>
/// Advances three rank populations under the alternating round-robin co-evolution contract
/// (WI-A4-29, implementation/track-a-a4-hierarchical-ai.md): one rank per generation, the
/// other two playing as deterministic partners (genome index 0 of each current population,
/// which elitism keeps as that rank's best-known genome).
/// </summary>
public sealed class HierarchicalPolicyEvolutionRunner
{
    private readonly IHierarchicalPolicyMatchEvaluator _evaluator;

    public NeuralEvolutionConfig Config { get; }

    public HierarchicalPolicyEvolutionRunner(IHierarchicalPolicyMatchEvaluator evaluator, NeuralEvolutionConfig config)
    {
        config.Validate();
        _evaluator = evaluator;
        Config = config;
    }

    /// <summary>Initializes all three rank populations with their frozen genome lengths.</summary>
    public IReadOnlyDictionary<NeuralRankKind, NeuralPopulation> InitializePopulations()
    {
        var populations = new Dictionary<NeuralRankKind, NeuralPopulation>();
        foreach (var rank in Enum.GetValues<NeuralRankKind>())
        {
            populations[rank] = NeuralEvolutionOperators.Initialize(RankConfig(rank));
        }

        return populations;
    }

    public HierarchicalGenerationResult RunGeneration(
        IReadOnlyDictionary<NeuralRankKind, NeuralPopulation> populations,
        int generation)
    {
        foreach (var (rank, population) in populations)
        {
            if (population.Genomes.Count != Config.PopulationSize)
            {
                throw new InvalidOperationException(
                    $"Rank {rank} has {population.Genomes.Count} genomes; expected {Config.PopulationSize}.");
            }
        }

        var advancingRank = HierarchicalEvolutionSchedule.AdvancingRank(generation);
        var partners = PartnerGenomes(populations, advancingRank);

        // Reuse the flat runner for evaluation, ranking, and advancement, scoped to the
        // advancing rank's genome length; match seeds follow the hierarchical derivation.
        var flatRunner = new NeuralPolicyEvolutionRunner(
            new RankScopedEvaluator(_evaluator, advancingRank, partners),
            RankConfig(advancingRank));
        var result = flatRunner.RunGeneration(
            populations[advancingRank],
            generation,
            (genomeIndex, slot) => NeuralEvolutionOperators.HierarchicalMatchSeed(
                Config, (uint)generation, advancingRank, genomeIndex, slot));

        var report = result.Report;
        report.AdvancingRank = advancingRank.ToString();
        report.PartnerGenomeIndices = new Dictionary<string, int>
        {
            [nameof(NeuralRankKind.General)] = advancingRank == NeuralRankKind.General ? -1 : 0,
            [nameof(NeuralRankKind.Commander)] = advancingRank == NeuralRankKind.Commander ? -1 : 0,
            [nameof(NeuralRankKind.Soldier)] = advancingRank == NeuralRankKind.Soldier ? -1 : 0,
        };

        var next = new Dictionary<NeuralRankKind, NeuralPopulation>(populations)
        {
            [advancingRank] = result.NextPopulation,
        };

        return new HierarchicalGenerationResult(report, advancingRank, next);
    }

    private NeuralEvolutionConfig RankConfig(NeuralRankKind rank) =>
        Config with { GenomeLength = HierarchicalEvolutionSchedule.GenomeLengthOf(rank) };

    private static IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> PartnerGenomes(
        IReadOnlyDictionary<NeuralRankKind, NeuralPopulation> populations,
        NeuralRankKind advancingRank)
    {
        var partners = new Dictionary<NeuralRankKind, IReadOnlyList<float>>();
        foreach (var (rank, population) in populations)
        {
            if (rank != advancingRank)
            {
                partners[rank] = population.Genomes[0].Weights;
            }
        }

        return partners;
    }

    private sealed class RankScopedEvaluator : INeuralPolicyMatchEvaluator
    {
        private readonly IHierarchicalPolicyMatchEvaluator _inner;
        private readonly NeuralRankKind _rank;
        private readonly IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> _partners;

        public RankScopedEvaluator(
            IHierarchicalPolicyMatchEvaluator inner,
            NeuralRankKind rank,
            IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> partners)
        {
            _inner = inner;
            _rank = rank;
            _partners = partners;
        }

        public NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed) =>
            _inner.Evaluate(_rank, genomeIndex, genome, _partners, matchSeed);
    }
}
