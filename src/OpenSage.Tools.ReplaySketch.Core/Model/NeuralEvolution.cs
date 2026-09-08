#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic.AI;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Configuration for neural-policy evolution (WI-A4-11 design,
/// implementation/track-a-a4-ai-policy-evolution.md). Immutable; validated by
/// <see cref="Validate"/>. Match fields are consumed by the evaluator adapter.
/// </summary>
public sealed record NeuralEvolutionConfig
{
    public uint ExperimentSeed { get; init; } = 1;

    public int PopulationSize { get; init; } = 8;

    public int MatchSeedCount { get; init; } = 2;

    public int EliteCount { get; init; } = 2;

    public int TournamentSize { get; init; } = 3;

    public float MutationProbability { get; init; } = 0.1f;

    public float MutationSigma { get; init; } = 0.25f;

    public float InitializationSigma { get; init; } = 0.5f;

    public float WeightClamp { get; init; } = 4f;

    public int MaxGenerations { get; init; } = 10;

    public int StopAfterStaleGenerations { get; init; } = 3;

    public int MatchFrameCap { get; init; } = 5400;

    public string MapPath { get; init; } = "maps/alpine assault/alpine assault.map";

    public string GenomeFaction { get; init; } = "FactionAmerica";

    public string OpponentFaction { get; init; } = "FactionGLA";

    public void Validate()
    {
        if (PopulationSize is < 2 or > 256)
        {
            throw new InvalidOperationException($"{nameof(PopulationSize)} must be 2..256.");
        }
        if (MatchSeedCount < 1)
        {
            throw new InvalidOperationException($"{nameof(MatchSeedCount)} must be at least 1.");
        }
        if (EliteCount < 1 || EliteCount >= PopulationSize)
        {
            throw new InvalidOperationException($"{nameof(EliteCount)} must be at least 1 and less than {nameof(PopulationSize)}.");
        }
        if (TournamentSize < 2 || TournamentSize > PopulationSize)
        {
            throw new InvalidOperationException($"{nameof(TournamentSize)} must be 2..{nameof(PopulationSize)}.");
        }
        if (MutationProbability <= 0f || MutationProbability > 1f)
        {
            throw new InvalidOperationException($"{nameof(MutationProbability)} must be in (0, 1].");
        }
        if (MutationSigma <= 0f || InitializationSigma <= 0f || WeightClamp <= 0f)
        {
            throw new InvalidOperationException("Sigmas and clamp must be positive.");
        }
        if (MaxGenerations < 1)
        {
            throw new InvalidOperationException($"{nameof(MaxGenerations)} must be at least 1.");
        }
        if (StopAfterStaleGenerations < 0)
        {
            throw new InvalidOperationException($"{nameof(StopAfterStaleGenerations)} must be 0 or more.");
        }
        if (MatchFrameCap < 1)
        {
            throw new InvalidOperationException($"{nameof(MatchFrameCap)} must be at least 1.");
        }
    }
}

/// <summary>One genome: exactly the v1 policy's weight vector (125 floats).</summary>
public sealed class NeuralGenome
{
    public int Index { get; }

    public float[] Weights { get; }

    public NeuralGenome(int index, IReadOnlyList<float> weights)
    {
        if (weights.Count != NeuralNetworkAIDecisionPolicy.WeightCount)
        {
            throw new ArgumentException(
                $"A neural genome requires exactly {NeuralNetworkAIDecisionPolicy.WeightCount} weights; got {weights.Count}.",
                nameof(weights));
        }

        Index = index;
        Weights = weights.ToArray();
    }
}

/// <summary>One generation's population, indexed 0..PopulationSize-1.</summary>
public sealed class NeuralPopulation
{
    public IReadOnlyList<NeuralGenome> Genomes { get; }

    public NeuralPopulation(IReadOnlyList<NeuralGenome> genomes)
    {
        Genomes = genomes;
    }
}

/// <summary>A genome with its aggregated fitness, pre-ranked best-first by the runner.</summary>
public sealed record RankedGenome(NeuralGenome Genome, double? Fitness, bool Failed);

/// <summary>Ancestry of one next-generation genome: its parent (or itself for elites).</summary>
public sealed record GenomeAncestry(int Index, int ParentIndex, bool Elite);

/// <summary>The next population plus per-genome ancestry.</summary>
public sealed record NeuralPopulationAdvance(
    NeuralPopulation Next,
    IReadOnlyList<GenomeAncestry> Ancestry);
