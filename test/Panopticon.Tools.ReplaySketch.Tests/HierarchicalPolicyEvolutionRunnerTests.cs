using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A4-30: the alternating round-robin schedule, deterministic partner sampling, per-rank
/// advancement and lengths, stamped reports, and failure semantics — against a controlled
/// evaluator, no installation required.
/// </summary>
public class HierarchicalPolicyEvolutionRunnerTests
{
    private sealed class FakeHierarchyEvaluator : IHierarchicalPolicyMatchEvaluator
    {
        public List<(NeuralRankKind Rank, int GenomeIndex, uint Seed, IReadOnlyList<float> General, IReadOnlyList<float> Commander, IReadOnlyList<float> Soldier)> Calls { get; } = [];

        public Func<int, NeuralMatchEvaluation>? EvaluateImpl { get; set; }

        public NeuralMatchEvaluation Evaluate(
            NeuralRankKind advancingRank,
            int genomeIndex,
            IReadOnlyList<float> genome,
            IReadOnlyDictionary<NeuralRankKind, IReadOnlyList<float>> partners,
            uint matchSeed)
        {
            Calls.Add((advancingRank, genomeIndex, matchSeed,
                partners.GetValueOrDefault(NeuralRankKind.General) ?? genome,
                partners.GetValueOrDefault(NeuralRankKind.Commander) ?? genome,
                partners.GetValueOrDefault(NeuralRankKind.Soldier) ?? genome));

            if (EvaluateImpl is not null)
            {
                return EvaluateImpl(genomeIndex);
            }

            return FrameCapped();
        }

        public static NeuralMatchEvaluation FrameCapped()
        {
            var output = new RunOutput
            {
                TotalLogicFrames = 5400,
                Outcome = new RunOutcomeRecord { Status = OutcomeStatus.InProgress },
            };
            output.MetricSamples.Add(new RunMetricSample
            {
                Players = { new RunPlayerMetrics { Player = "Genome", Money = 1000u, OwnedObjects = 5 } },
            });
            return new NeuralMatchEvaluation(output, "Genome");
        }
    }

    private static NeuralEvolutionConfig Config(int populationSize = 3, int matchSeeds = 1) =>
        new()
        {
            ExperimentSeed = 9u,
            PopulationSize = populationSize,
            MatchSeedCount = matchSeeds,
            EliteCount = 1,
            TournamentSize = 2,
            MatchFrameCap = 5400,
        };

    [Fact]
    public void Schedule_AlternatesGeneralCommanderSoldier()
    {
        Assert.Equal(NeuralRankKind.General, HierarchicalEvolutionSchedule.AdvancingRank(0));
        Assert.Equal(NeuralRankKind.Commander, HierarchicalEvolutionSchedule.AdvancingRank(1));
        Assert.Equal(NeuralRankKind.Soldier, HierarchicalEvolutionSchedule.AdvancingRank(2));
        Assert.Equal(NeuralRankKind.General, HierarchicalEvolutionSchedule.AdvancingRank(3));
    }

    [Fact]
    public void InitializePopulations_UsesPerRankGenomeLengths()
    {
        var runner = new HierarchicalPolicyEvolutionRunner(new FakeHierarchyEvaluator(), Config());

        var populations = runner.InitializePopulations();

        Assert.Equal(3, populations.Count);
        Assert.All(populations[NeuralRankKind.General].Genomes,
            g => Assert.Equal(HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.General), g.Weights.Length));
        Assert.All(populations[NeuralRankKind.Commander].Genomes,
            g => Assert.Equal(HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.Commander), g.Weights.Length));
        Assert.All(populations[NeuralRankKind.Soldier].Genomes,
            g => Assert.Equal(HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.Soldier), g.Weights.Length));
    }

    [Fact]
    public void RunGeneration_EvaluatesExactlyPopulationTimesSeeds_WithHierarchicalSeeds()
    {
        var evaluator = new FakeHierarchyEvaluator();
        var config = Config(populationSize: 3, matchSeeds: 2);
        var runner = new HierarchicalPolicyEvolutionRunner(evaluator, config);
        var populations = runner.InitializePopulations();

        var result = runner.RunGeneration(populations, generation: 0);

        Assert.Equal(6, evaluator.Calls.Count);
        Assert.All(evaluator.Calls, call => Assert.Equal(NeuralRankKind.General, call.Rank));
        var expectedSeeds = Enumerable.Range(0, 2)
            .Select(slot => NeuralEvolutionOperators.HierarchicalMatchSeed(config, 0, NeuralRankKind.General, 1, slot))
            .ToList();
        Assert.Equal(expectedSeeds, evaluator.Calls.Where(c => c.GenomeIndex == 1).Select(c => c.Seed).ToList());
        Assert.Equal("General", result.Report.AdvancingRank);
    }

    [Fact]
    public void Partners_AreGenomeZeroOfEachNonAdvancingRank()
    {
        var evaluator = new FakeHierarchyEvaluator();
        var runner = new HierarchicalPolicyEvolutionRunner(evaluator, Config());
        var populations = runner.InitializePopulations();
        // Distinguish the commanders' genome #0 so partner identity is observable.
        populations = new Dictionary<NeuralRankKind, NeuralPopulation>(populations)
        {
            [NeuralRankKind.Commander] = new NeuralPopulation(
                populations[NeuralRankKind.Commander].Genomes.Select((g, i) => new NeuralGenome(g.Index, MarkFirst(g, i), g.Weights.Length)).ToList()),
        };

        runner.RunGeneration(populations, generation: 0);

        Assert.All(evaluator.Calls, call => Assert.True(call.Commander[0] == 123f));
        Assert.All(evaluator.Calls, call => Assert.Equal(
            populations[NeuralRankKind.Soldier].Genomes[0].Weights.ToArray(),
            call.Soldier.ToArray()));
        // The advancing rank itself partners with its own evaluated genome.
        Assert.All(evaluator.Calls, call => Assert.Equal(
            HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.General),
            call.General.Count));

        static float[] MarkFirst(NeuralGenome genome, int index)
        {
            var weights = genome.Weights.ToArray();
            if (index == 0)
            {
                weights[0] = 123f;
            }

            return weights;
        }
    }

    [Fact]
    public void RunGeneration_AdvancesOnlyTheAdvancingRank()
    {
        var evaluator = new FakeHierarchyEvaluator
        {
            EvaluateImpl = genomeIndex => genomeIndex == 0
                ? FakeHierarchyEvaluator.FrameCapped()
                : throw new InvalidOperationException("match exploded"),
        };
        var runner = new HierarchicalPolicyEvolutionRunner(evaluator, Config(populationSize: 2));
        var populations = runner.InitializePopulations();
        var soldierBefore = populations[NeuralRankKind.Soldier];

        var result = runner.RunGeneration(populations, generation: 1);

        Assert.Equal(NeuralRankKind.Commander, result.AdvancedRank);
        Assert.Equal("Commander", result.Report.AdvancingRank);
        Assert.NotSame(populations[NeuralRankKind.Commander], result.Populations[NeuralRankKind.Commander]);
        Assert.Same(soldierBefore, result.Populations[NeuralRankKind.Soldier]);
        Assert.All(result.Populations[NeuralRankKind.Commander].Genomes,
            g => Assert.Equal(HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.Commander), g.Weights.Length));

        // The failing genome is marked and the report is still produced.
        var failed = result.Report.Genomes.Single(g => g.Failed);
        Assert.Null(failed.Fitness);
        Assert.Contains(failed.Matches, m => m.Failed && m.Error == "match exploded");
        Assert.NotNull(result.Report.Summary.Best);
    }

    [Fact]
    public void Report_PartnerIndicesStamped_AndRoundTripsThroughJson()
    {
        var evaluator = new FakeHierarchyEvaluator();
        var runner = new HierarchicalPolicyEvolutionRunner(evaluator, Config());
        var populations = runner.InitializePopulations();

        var report = runner.RunGeneration(populations, generation: 2).Report;

        Assert.Equal("Soldier", report.AdvancingRank);
        Assert.Equal(-1, report.PartnerGenomeIndices!["Soldier"]);
        Assert.Equal(0, report.PartnerGenomeIndices["General"]);
        Assert.Equal(0, report.PartnerGenomeIndices["Commander"]);

        var reloaded = NeuralGenerationReport.FromJson(report.ToJson());
        Assert.Equal(report.AdvancingRank, reloaded!.AdvancingRank);
        Assert.Equal(report.PartnerGenomeIndices, reloaded.PartnerGenomeIndices);
    }
}
