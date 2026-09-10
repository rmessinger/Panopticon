#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

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
