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
