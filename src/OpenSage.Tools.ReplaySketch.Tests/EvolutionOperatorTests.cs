using System;
using System.Linq;
using OpenSage.Logic.AI;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A4-13: deterministic neural population and variation operators. Same inputs and
/// seed produce byte-identical next-generation genomes; elites are preserved exactly;
/// mutation and selection follow the documented draw order (fixtures mirror the design's
/// algorithm so any reordering of draws fails).
/// </summary>
public class EvolutionOperatorTests
{
    private static NeuralEvolutionConfig Config(int populationSize = 4, int eliteCount = 1, int tournamentSize = 2) =>
        new()
        {
            ExperimentSeed = 777u,
            PopulationSize = populationSize,
            EliteCount = eliteCount,
            TournamentSize = tournamentSize,
        };

    [Fact]
    public void Initialize_IsDeterministicPerSeed_AndProducesValidGenomes()
    {
        var config = Config();

        var first = NeuralEvolutionOperators.Initialize(config);
        var second = NeuralEvolutionOperators.Initialize(config);

        Assert.Equal(config.PopulationSize, first.Genomes.Count);
        for (var i = 0; i < config.PopulationSize; i++)
        {
            Assert.Equal(i, first.Genomes[i].Index);
            Assert.Equal(NeuralNetworkAIDecisionPolicy.WeightCount, first.Genomes[i].Weights.Length);
            Assert.All(first.Genomes[i].Weights, w => Assert.True(MathF.Abs(w) <= config.WeightClamp + 1e-6f));
            Assert.True(first.Genomes[i].Weights.SequenceEqual(second.Genomes[i].Weights));
        }
    }

    [Fact]
    public void Initialize_DifferentSeed_ProducesDifferentGenomes()
    {
        var first = NeuralEvolutionOperators.Initialize(Config());
        var second = NeuralEvolutionOperators.Initialize(Config() with { ExperimentSeed = 778u });

        Assert.False(first.Genomes[0].Weights.SequenceEqual(second.Genomes[0].Weights));
    }

    [Fact]
    public void InvalidConfigs_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => NeuralEvolutionOperators.Initialize(Config(populationSize: 1)));
        Assert.Throws<InvalidOperationException>(() => NeuralEvolutionOperators.Initialize(Config(eliteCount: 0)));
        Assert.Throws<InvalidOperationException>(() => NeuralEvolutionOperators.Initialize(Config(populationSize: 4, eliteCount: 4)));
        Assert.Throws<InvalidOperationException>(() => NeuralEvolutionOperators.Initialize(Config(tournamentSize: 1)));
        Assert.Throws<InvalidOperationException>(() => NeuralEvolutionOperators.Initialize(Config() with { MutationSigma = 0f }));
    }

    [Fact]
    public void Advance_RejectsWrongPopulationOrGenomeSize()
    {
        var config = Config(populationSize: 4);
        var ranked = Enumerable.Range(0, 3)
            .Select(i => new RankedGenome(new NeuralGenome(i, new float[NeuralNetworkAIDecisionPolicy.WeightCount]), 0d, false))
            .ToList();

        Assert.Throws<InvalidOperationException>(
            () => NeuralEvolutionOperators.Advance(ranked, config, generation: 1));
    }

    [Fact]
    public void Advance_PreservesElitesExactly_WithAncestry()
    {
        var config = Config(populationSize: 4, eliteCount: 2);
        var population = NeuralEvolutionOperators.Initialize(config);
        var ranked = population.Genomes
            .Select((g, i) => new RankedGenome(g, 100d - i, false))
            .ToList();

        var advance = NeuralEvolutionOperators.Advance(ranked, config, generation: 3);

        Assert.True(advance.Next.Genomes[0].Weights.SequenceEqual(ranked[0].Genome.Weights));
        Assert.True(advance.Next.Genomes[1].Weights.SequenceEqual(ranked[1].Genome.Weights));
        Assert.Equal([new GenomeAncestry(0, ranked[0].Genome.Index, true), new GenomeAncestry(1, ranked[1].Genome.Index, true)],
            advance.Ancestry.Take(2));
        Assert.All(advance.Ancestry.Skip(2), a => Assert.False(a.Elite));
        Assert.All(advance.Ancestry.Skip(2), a => Assert.Contains(a.ParentIndex, ranked.Select(r => r.Genome.Index)));
    }

    [Fact]
    public void Advance_IsByteIdenticalForSameSeedAndInputs()
    {
        var config = Config(populationSize: 6, eliteCount: 2, tournamentSize: 3);
        var population = NeuralEvolutionOperators.Initialize(config);
        var ranked = population.Genomes
            .Select((g, i) => new RankedGenome(g, 50d - i * 1.5, false))
            .ToList();

        var first = NeuralEvolutionOperators.Advance(ranked, config, generation: 2);
        var second = NeuralEvolutionOperators.Advance(ranked, config, generation: 2);

        for (var i = 0; i < config.PopulationSize; i++)
        {
            Assert.True(first.Next.Genomes[i].Weights.SequenceEqual(second.Next.Genomes[i].Weights));
        }

        Assert.Equal(first.Ancestry, second.Ancestry);
    }

    [Fact]
    public void Advance_DifferentGenerationSeed_ProducesDifferentChildren()
    {
        var config = Config(populationSize: 6, eliteCount: 1);
        var population = NeuralEvolutionOperators.Initialize(config);
        var ranked = population.Genomes.Select((g, i) => new RankedGenome(g, 50d - i, false)).ToList();

        var first = NeuralEvolutionOperators.Advance(ranked, config, generation: 2);
        var second = NeuralEvolutionOperators.Advance(ranked, config, generation: 3);

        Assert.False(first.Next.Genomes[^1].Weights.SequenceEqual(second.Next.Genomes[^1].Weights));
    }

    // Mirrors the design's documented algorithm: elites, then per child a tournament of
    // TournamentSize position draws (winner = lowest position), then per weight one draw,
    // plus two Box-Muller draws when mutating. Any change to the draw order diverges.
    [Fact]
    public void Advance_FollowsTheDocumentedDrawOrder()
    {
        var config = Config(populationSize: 4, eliteCount: 1, tournamentSize: 2) with
        {
            MutationProbability = 1f,
            MutationSigma = 0.5f,
        };
        var population = NeuralEvolutionOperators.Initialize(config);
        var ranked = population.Genomes.Select((g, i) => new RankedGenome(g, 40d - i, false)).ToList();

        var advance = NeuralEvolutionOperators.Advance(ranked, config, generation: 5);

        var rng = new Random((int)NeuralEvolutionOperators.OperatorSeed(config, 5));
        for (var slot = config.EliteCount; slot < config.PopulationSize; slot++)
        {
            var best = int.MaxValue;
            for (var draw = 0; draw < config.TournamentSize; draw++)
            {
                best = Math.Min(best, rng.Next(config.PopulationSize));
            }

            var parent = ranked[best].Genome;
            var expected = new float[parent.Weights.Length];
            for (var i = 0; i < expected.Length; i++)
            {
                var weight = parent.Weights[i];
                if (rng.NextDouble() < config.MutationProbability)
                {
                    var u1 = 1.0 - rng.NextDouble();
                    var u2 = rng.NextDouble();
                    var step = config.MutationSigma * MathF.Sqrt(-2f * MathF.Log((float)u1)) * MathF.Cos(2f * MathF.PI * (float)u2);
                    weight = MathF.Max(-config.WeightClamp, MathF.Min(config.WeightClamp, weight + step));
                }

                expected[i] = weight;
            }

            Assert.True(expected.SequenceEqual(advance.Next.Genomes[slot].Weights),
                $"Child {slot} diverged from the documented draw order.");
            Assert.Equal(parent.Index, advance.Ancestry[slot].ParentIndex);
        }
    }

    [Fact]
    public void MatchSeed_IsStableAndDerivedFromAllInputs()
    {
        var config = Config();

        var seed = NeuralEvolutionOperators.MatchSeed(config, generation: 2, genomeIndex: 3, slot: 1);

        Assert.Equal(seed, NeuralEvolutionOperators.MatchSeed(config, 2, 3, 1));
        Assert.NotEqual(seed, NeuralEvolutionOperators.MatchSeed(config, 3, 3, 1));
        Assert.NotEqual(seed, NeuralEvolutionOperators.MatchSeed(config, 2, 4, 1));
        Assert.NotEqual(seed, NeuralEvolutionOperators.MatchSeed(config, 2, 3, 2));
    }

    [Fact]
    public void Genome_RejectsWrongWeightCount()
    {
        Assert.Throws<ArgumentException>(() => new NeuralGenome(0, new float[124]));
    }
}
