#nullable enable

using System;
using System.Collections.Generic;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Deterministic population and variation operators for neural-policy evolution
/// (WI-A4-13, design implementation/track-a-a4-ai-policy-evolution.md §2). All randomness
/// flows through explicitly seeded <see cref="Random"/> instances with a fixed draw order,
/// so the same inputs and seed produce byte-identical next-generation genomes. Game-free:
/// no construction, fitness scoring, or UI.
/// </summary>
public static class NeuralEvolutionOperators
{
    /// <summary>Wrapping uint seed mixing, exactly as specified by the design.</summary>
    public static uint Mix(uint a, uint b, uint c, uint d) =>
        (a * 73856093u) ^ (b * 19349663u) ^ (c * 83492791u) ^ (d * 2971215073u);

    public static uint OperatorSeed(NeuralEvolutionConfig config, uint generation) =>
        Mix(config.ExperimentSeed, generation, 0, 0);

    public static uint MatchSeed(NeuralEvolutionConfig config, uint generation, int genomeIndex, int slot) =>
        Mix(config.ExperimentSeed, generation, (uint)(genomeIndex + 1), (uint)(slot + 1));

    /// <summary>
    /// Initializes a population: one Random over the experiment seed, genomes in index
    /// order, weights in index order, each clamped to ±WeightClamp.
    /// </summary>
    public static NeuralPopulation Initialize(NeuralEvolutionConfig config)
    {
        config.Validate();

        var rng = new Random((int)config.ExperimentSeed);
        var genomes = new List<NeuralGenome>(config.PopulationSize);
        for (var i = 0; i < config.PopulationSize; i++)
        {
            genomes.Add(new NeuralGenome(i, CreateWeights(config, rng)));
        }

        return new NeuralPopulation(genomes);
    }

    private static float[] CreateWeights(NeuralEvolutionConfig config, Random rng)
    {
        var weights = new float[GenomeLength];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = Clamp(NextGaussian(rng, config.InitializationSigma), config.WeightClamp);
        }

        return weights;
    }

    private static int GenomeLength => Logic.AI.NeuralNetworkAIDecisionPolicy.WeightCount;

    /// <summary>
    /// Advances one generation. <paramref name="rankedByFitness"/> must already be ordered
    /// best-first (fitness descending, ties by lower genome index; failed genomes last).
    /// Elites are copied verbatim; every other child is a mutated tournament-selection
    /// clone, with the fixed draw order from the design.
    /// </summary>
    public static NeuralPopulationAdvance Advance(
        IReadOnlyList<RankedGenome> rankedByFitness,
        NeuralEvolutionConfig config,
        uint generation)
    {
        config.Validate();
        if (rankedByFitness.Count != config.PopulationSize)
        {
            throw new InvalidOperationException(
                $"Advance expects {config.PopulationSize} ranked genomes; got {rankedByFitness.Count}.");
        }
        foreach (var ranked in rankedByFitness)
        {
            if (ranked.Genome.Weights.Length != GenomeLength)
            {
                throw new InvalidOperationException(
                    $"Genome {ranked.Genome.Index} has {ranked.Genome.Weights.Length} weights; expected {GenomeLength}.");
            }
        }

        var rng = new Random((int)OperatorSeed(config, generation));

        var next = new List<NeuralGenome>(config.PopulationSize);
        var ancestry = new List<GenomeAncestry>(config.PopulationSize);

        for (var slot = 0; slot < config.EliteCount; slot++)
        {
            var elite = rankedByFitness[slot].Genome;
            next.Add(new NeuralGenome(slot, elite.Weights));
            ancestry.Add(new GenomeAncestry(slot, elite.Index, Elite: true));
        }

        for (var slot = config.EliteCount; slot < config.PopulationSize; slot++)
        {
            var parent = SelectTournamentWinner(rankedByFitness, config, rng);
            next.Add(new NeuralGenome(slot, Mutate(parent.Weights, config, rng)));
            ancestry.Add(new GenomeAncestry(slot, parent.Index, Elite: false));
        }

        return new NeuralPopulationAdvance(new NeuralPopulation(next), ancestry);
    }

    // Winner = best (lowest) position among TournamentSize uniform draws; ties by position,
    // matching the ranked order the caller produced.
    private static NeuralGenome SelectTournamentWinner(
        IReadOnlyList<RankedGenome> rankedByFitness, NeuralEvolutionConfig config, Random rng)
    {
        var best = int.MaxValue;
        for (var draw = 0; draw < config.TournamentSize; draw++)
        {
            var position = rng.Next(rankedByFitness.Count);
            if (position < best)
            {
                best = position;
            }
        }

        return rankedByFitness[best].Genome;
    }

    private static float[] Mutate(float[] parent, NeuralEvolutionConfig config, Random rng)
    {
        var weights = new float[parent.Length];
        for (var i = 0; i < parent.Length; i++)
        {
            var weight = parent[i];
            if (rng.NextDouble() < config.MutationProbability)
            {
                weight = Clamp(weight + NextGaussian(rng, config.MutationSigma), config.WeightClamp);
            }

            weights[i] = weight;
        }

        return weights;
    }

    // Box-Muller with u1 in (0, 1] so the log is always finite (design §2).
    internal static float NextGaussian(Random rng, float sigma)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return sigma * MathF.Sqrt(-2f * MathF.Log((float)u1)) * MathF.Cos(2f * MathF.PI * (float)u2);
    }

    private static float Clamp(float value, float bound) =>
        MathF.Max(-bound, MathF.Min(bound, value));
}
