using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic;
using OpenSage.Logic.AI;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A4-16: neural-policy training plumbing. The non-interactive tests run the full
/// configuration path with a controlled evaluator (population init through generation
/// advancement and report serialization); the Interactive smoke test drives the real
/// headless match evaluator over a small population.
/// </summary>
public class NeuralPolicyEvolutionIntegrationTests
{
    private sealed class ControlledEvaluator : INeuralPolicyMatchEvaluator
    {
        public List<(int Genome, uint Seed)> Calls { get; } = [];

        public NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed)
        {
            Calls.Add((genomeIndex, matchSeed));

            // Frame-capped draws: deterministic, and the genome player is present.
            var output = new RunOutput
            {
                TotalLogicFrames = 5400,
                Outcome = new RunOutcomeRecord { Status = OutcomeStatus.InProgress },
            };
            output.MetricSamples.Add(new RunMetricSample
            {
                Players = { new RunPlayerMetrics { Player = HeadlessNeuralMatchEvaluator.GenomePlayerName, Money = 1000u, OwnedObjects = 5 } },
            });
            return new NeuralMatchEvaluation(output, HeadlessNeuralMatchEvaluator.GenomePlayerName);
        }
    }

    [Fact]
    public void FullConfigurationPath_ChainsGenerations_ThroughTheRunner()
    {
        var config = new NeuralEvolutionConfig
        {
            ExperimentSeed = 5u,
            PopulationSize = 4,
            MatchSeedCount = 1,
            EliteCount = 2,
            TournamentSize = 2,
            MatchFrameCap = 5400,
        };
        var evaluator = new ControlledEvaluator();
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);

        var population = NeuralEvolutionOperators.Initialize(config);
        var bestEver = double.MinValue;
        var staleGenerations = 0;
        NeuralGenerationReport? report = null;
        var generationsRun = 0;

        for (var generation = 0; generation < config.MaxGenerations; generation++)
        {
            generationsRun++;
            var result = runner.RunGeneration(population, generation);
            population = result.NextPopulation;
            report = result.Report;

            var best = report.Summary.Best!.Value;
            if (best > bestEver)
            {
                bestEver = best;
                staleGenerations = 0;
            }
            else
            {
                staleGenerations++;
                if (staleGenerations >= config.StopAfterStaleGenerations)
                {
                    break;
                }
            }
        }

        // Full plumbing: chained populations stayed sized, every generation evaluated all
        // genomes, reports round-trip, and the loop respected the generation budget.
        Assert.NotNull(report);
        Assert.Equal(generationsRun * config.PopulationSize, evaluator.Calls.Count);
        Assert.Equal(config.PopulationSize, population.Genomes.Count);
        Assert.All(population.Genomes, g => Assert.Equal(NeuralNetworkAIDecisionPolicy.WeightCount, g.Weights.Length));
        Assert.Equal(report!.ToJson(), NeuralGenerationReport.FromJson(report.ToJson())!.ToJson());
    }

    [Fact]
    public void PayloadPath_GenomeWeightsReachTheFactory()
    {
        // The payload leg of the plumbing, without an install: a 125-float payload selects
        // the neural policy through the same factory the evaluator uses.
        var genome = new float[NeuralNetworkAIDecisionPolicy.WeightCount];
        var payload = new AIDecisionPolicyPayload
        {
            Type = AIDecisionPolicyType.NeuralNetwork,
            NeuralNetworkWeights = genome,
        };

        var policy = AIDecisionPolicyFactory.Create(payload);

        Assert.IsType<NeuralNetworkAIDecisionPolicy>(policy);
    }
}

/// <summary>
/// WI-A4-16 Interactive smoke: a small population over one short real match each, through
/// the real headless evaluator. Requires a Generals installation (CNC_GENERALS_PATH).
/// </summary>
public class NeuralPolicyEvolutionInteractiveTests
{
    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void SmallPopulation_OneGenerationThroughRealHeadlessMatches_ProducesReport()
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            throw new InvalidOperationException("Generals installation not found for Interactive test.");
        }

        var config = new NeuralEvolutionConfig
        {
            ExperimentSeed = 42u,
            PopulationSize = 2,
            MatchSeedCount = 1,
            EliteCount = 1,
            TournamentSize = 2,
            MatchFrameCap = 900,
        };
        var evaluator = new HeadlessNeuralMatchEvaluator(installation, config);
        var runner = new NeuralPolicyEvolutionRunner(evaluator, config);

        var population = NeuralEvolutionOperators.Initialize(config);
        var result = runner.RunGeneration(population, generation: 0);

        var report = result.Report;
        Assert.Equal(config.PopulationSize, report.Genomes.Count);
        Assert.Equal(config.PopulationSize, result.NextPopulation.Genomes.Count);
        Assert.Equal(config.ExperimentSeed, report.ExperimentSeed);

        // Each genome's match ran the neural policy in a real match: an outcome record and
        // metric samples for the genome player exist, and the report scored them.
        foreach (var genome in report.Genomes)
        {
            var match = Assert.Single(genome.Matches);
            Assert.False(match.Failed, match.Error);
            Assert.NotNull(match.Outcome);
        }

        Assert.All(report.Genomes, g => Assert.False(g.Failed));
        Assert.NotNull(report.Summary.Best);
        Assert.True(report.Summary.Diversity > 0);
        Assert.NotEmpty(report.NextGeneration);
        Assert.Equal(
            NeuralEvolutionOperators.MatchSeed(config, 0, report.Genomes[0].Index, 0),
            report.MatchSeeds[0]);
    }
}
