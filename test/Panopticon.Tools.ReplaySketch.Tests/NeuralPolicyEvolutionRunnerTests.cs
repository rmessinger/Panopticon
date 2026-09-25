using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class NeuralPolicyEvolutionRunnerTests
{
    private sealed class FakeEvaluator : INeuralPolicyMatchEvaluator
    {
        public List<(int GenomeIndex, uint MatchSeed)> Calls { get; } = [];

        public Func<int, uint, NeuralMatchEvaluation>? EvaluateImpl { get; set; }

        public NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed)
        {
            Calls.Add((genomeIndex, matchSeed));

            if (EvaluateImpl is not null)
            {
                return EvaluateImpl(genomeIndex, matchSeed);
            }

            return Evaluation(win: false, frames: 5400, money: 5000u, owned: 15);
        }
    }

    private static NeuralMatchEvaluation Evaluation(bool win, uint frames, uint money, int owned)
    {
        var output = new RunOutput
        {
            TotalLogicFrames = frames,
            Outcome = new RunOutcomeRecord
            {
                Status = win ? OutcomeStatus.Decided : OutcomeStatus.InProgress,
                WinningPlayers = win ? ["Genome"] : [],
                DecidedOnFrame = win ? (int?)1200 : null,
            },
        };
        output.MetricSamples.Add(new RunMetricSample
        {
            Frame = frames,
            Players = { new RunPlayerMetrics { Player = "Genome", Money = money, OwnedObjects = owned } },
        });
        return new NeuralMatchEvaluation(output, "Genome");
    }

    private static NeuralEvolutionConfig Config(int populationSize = 4, int matchSeeds = 2) =>
        new() { ExperimentSeed = 42u, PopulationSize = populationSize, MatchSeedCount = matchSeeds, MatchFrameCap = 5400 };

    [Fact]
    public void RunGeneration_PerformsExactlyPopulationTimesSeedEvaluations()
    {
        var evaluator = new FakeEvaluator();
        var config = Config(populationSize: 4, matchSeeds: 2);
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var result = runner.RunGeneration(population, generation: 0);

        Assert.Equal(8, evaluator.Calls.Count);
        var expectedSeeds = Enumerable.Range(0, config.MatchSeedCount)
            .Select(slot => NeuralEvolutionOperators.MatchSeed(config, 0, 0, slot))
            .ToList();
        Assert.Equal(expectedSeeds, result.Report.MatchSeeds);
        Assert.All(result.Report.Genomes, g => Assert.Equal(config.MatchSeedCount, g.Matches.Count));
    }

    [Fact]
    public void RunGeneration_RanksByFitnessDescending_TiesByLowerIndex()
    {
        var evaluator = new FakeEvaluator
        {
            // Genome 3 wins (high fitness), genomes 0 and 1 tie by losing, genome 2 frame-caps.
            EvaluateImpl = (genome, seed) => genome switch
            {
                3 => Evaluation(win: true, frames: 1200, money: 5000, owned: 15),
                2 => Evaluation(win: false, frames: 5400, money: 5000, owned: 15),
                _ => Evaluation(win: false, frames: 5400, money: 0, owned: 0),
            },
        };
        var config = Config(populationSize: 4);
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var report = runner.RunGeneration(population, generation: 0).Report;

        Assert.Equal(1, report.Genomes.Single(g => g.Index == 3).Rank);
        Assert.Equal(2, report.Genomes.Single(g => g.Index == 2).Rank);
        Assert.Equal(3, report.Genomes.Single(g => g.Index == 0).Rank);
        Assert.Equal(4, report.Genomes.Single(g => g.Index == 1).Rank);
        Assert.Equal(report.Genomes.Single(g => g.Index == 3).Fitness!.Value, report.Summary.Best!.Value);
        Assert.Equal(report.Genomes.Single(g => g.Index == 0).Fitness!.Value, report.Summary.Worst!.Value);
    }

    [Fact]
    public void RunGeneration_FailedEvaluation_MarksGenomeFailed_RanksLast_ReportStillProduced()
    {
        var evaluator = new FakeEvaluator
        {
            EvaluateImpl = (genome, seed) =>
                genome == 1 && seed == NeuralEvolutionOperators.MatchSeed(Config(), 0, 1, 0)
                    ? throw new InvalidOperationException("engine exploded")
                    : Evaluation(win: false, frames: 5400, money: 5000, owned: 15),
        };
        var config = Config(populationSize: 3);
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var result = runner.RunGeneration(population, generation: 0);

        var failed = result.Report.Genomes.Single(g => g.Index == 1);
        Assert.True(failed.Failed);
        Assert.Null(failed.Fitness);
        Assert.Null(failed.Rank);
        Assert.Contains(failed.Matches, m => m.Failed && m.Error == "engine exploded");
        Assert.All(result.Report.Genomes.Where(g => g.Index != 1), g =>
        {
            Assert.False(g.Failed);
            Assert.NotNull(g.Rank);
        });
        Assert.NotNull(result.Report.Summary.Best);
        Assert.Equal(3, result.Report.Genomes.Count);
    }

    [Fact]
    public void RunGeneration_NextGenerationAncestry_MatchesEliteAndParentStructure()
    {
        var evaluator = new FakeEvaluator
        {
            EvaluateImpl = (genome, seed) => Evaluation(win: genome == 2, frames: 5400, money: 5000, owned: 15),
        };
        var config = Config(populationSize: 4, matchSeeds: 1) with { EliteCount = 1 };
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var result = runner.RunGeneration(population, generation: 0);

        Assert.Equal(config.PopulationSize, result.NextPopulation.Genomes.Count);
        Assert.Equal(config.PopulationSize, result.Report.NextGeneration.Count);
        var elite = result.Report.NextGeneration.Single(n => n.Elite);
        Assert.Equal(0, elite.Index);
        Assert.Equal(2, elite.ParentIndex);
        Assert.All(result.Report.NextGeneration.Where(n => !n.Elite),
            n => Assert.Contains(n.ParentIndex, Enumerable.Range(0, config.PopulationSize)));
    }

    [Fact]
    public void RunGeneration_ProducesNonZeroDiversity_ForInitializedPopulation()
    {
        var evaluator = new FakeEvaluator();
        var config = Config();
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var report = runner.RunGeneration(population, generation: 0).Report;

        Assert.True(report.Summary.Diversity > 0);
    }

    [Fact]
    public void RunGeneration_WrongPopulationSize_Throws()
    {
        var runner = new NeuralPolicyEvolutionRunner(new FakeEvaluator(), Config(populationSize: 4));
        var small = NeuralEvolutionOperators.Initialize(Config(populationSize: 2) with { EliteCount = 1, TournamentSize = 2 });

        Assert.Throws<InvalidOperationException>(() => runner.RunGeneration(small, generation: 0));
    }

    [Fact]
    public void GenerationReport_RoundTripsThroughJson()
    {
        var evaluator = new FakeEvaluator();
        var config = Config(populationSize: 4, matchSeeds: 2);
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);
        var population = NeuralEvolutionOperators.Initialize(config);

        var report = runner.RunGeneration(population, generation: 5).Report;
        var reloaded = NeuralGenerationReport.FromJson(report.ToJson());

        Assert.NotNull(reloaded);
        Assert.Equal(report.ExperimentSeed, reloaded!.ExperimentSeed);
        Assert.Equal(5, reloaded.Generation);
        Assert.Equal(report.PopulationSize, reloaded.PopulationSize);
        Assert.Equal(report.MatchSeeds, reloaded.MatchSeeds);
        Assert.Equal(report.Summary.Best, reloaded.Summary.Best);
        Assert.Equal(report.Summary.Diversity, reloaded.Summary.Diversity);
        Assert.Equal(report.Genomes.Count, reloaded.Genomes.Count);
        Assert.All(
            report.Genomes.Zip(reloaded.Genomes),
            pair =>
            {
                Assert.Equal(pair.First.Index, pair.Second.Index);
                Assert.Equal(pair.First.Rank, pair.Second.Rank);
                Assert.Equal(pair.First.Fitness, pair.Second.Fitness);
                Assert.Equal(pair.First.Matches.Count, pair.Second.Matches.Count);
                Assert.Equal(pair.First.Terms.Outcome, pair.Second.Terms.Outcome);
            });
        Assert.Equal(report.ToJson(), reloaded.ToJson());
    }
}
