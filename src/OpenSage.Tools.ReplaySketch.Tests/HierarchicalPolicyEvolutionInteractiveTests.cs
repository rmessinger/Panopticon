using System;
using OpenSage.Logic;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A4-30 Interactive smoke: one co-evolution generation over real headless matches. A
/// two-genome soldier population advances with general/commander partners from their
/// initialized populations; requires a Generals installation (CNC_GENERALS_PATH).
/// </summary>
public class HierarchicalPolicyEvolutionInteractiveTests
{
    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void OneGeneration_AdvancingSoldiers_OverRealHeadlessMatches()
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            throw new InvalidOperationException("Generals installation not found for Interactive test.");
        }

        var config = new NeuralEvolutionConfig
        {
            ExperimentSeed = 21u,
            PopulationSize = 2,
            MatchSeedCount = 1,
            EliteCount = 1,
            TournamentSize = 2,
            MatchFrameCap = 900,
        };
        var runner = new HierarchicalPolicyEvolutionRunner(
            new HeadlessHierarchicalMatchEvaluator(installation, config),
            config);
        var populations = runner.InitializePopulations();

        var result = runner.RunGeneration(populations, generation: 2);

        Assert.Equal(NeuralRankKind.Soldier, result.AdvancedRank);
        Assert.Equal(2, result.Report.Genomes.Count);
        foreach (var genome in result.Report.Genomes)
        {
            var match = Assert.Single(genome.Matches);
            Assert.False(match.Failed, match.Error);
            Assert.NotNull(match.Outcome);
        }

        Assert.All(result.Report.Genomes, g => Assert.False(g.Failed));
        Assert.NotNull(result.Report.Summary.Best);
        Assert.True(result.Report.Summary.Diversity > 0);
        Assert.Equal(
            HierarchicalEvolutionSchedule.GenomeLengthOf(NeuralRankKind.Soldier),
            result.Populations[NeuralRankKind.Soldier].Genomes[0].Weights.Length);
    }
}
