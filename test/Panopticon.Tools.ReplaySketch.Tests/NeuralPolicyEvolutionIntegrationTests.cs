using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

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
