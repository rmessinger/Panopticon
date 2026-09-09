#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>The next population plus per-genome ancestry.</summary>
public sealed record NeuralPopulationAdvance(
    NeuralPopulation Next,
    IReadOnlyList<GenomeAncestry> Ancestry);
