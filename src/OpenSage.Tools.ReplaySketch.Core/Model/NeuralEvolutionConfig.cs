#nullable enable

using System;

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

    /// <summary>Genome length this configuration evolves: the flat policy's 125 by default,
    /// or a per-rank length of the hierarchical contracts (general 116, commander 156,
    /// soldier 75).</summary>
    public int GenomeLength { get; init; } = OpenSage.Logic.AI.NeuralNetworkAIDecisionPolicy.WeightCount;

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
        if (GenomeLength < 1)
        {
            throw new InvalidOperationException($"{nameof(GenomeLength)} must be at least 1.");
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
