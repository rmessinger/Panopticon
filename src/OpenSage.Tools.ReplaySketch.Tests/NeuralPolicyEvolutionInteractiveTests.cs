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
