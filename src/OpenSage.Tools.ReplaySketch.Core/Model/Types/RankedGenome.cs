#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>A genome with its aggregated fitness, pre-ranked best-first by the runner.</summary>
public sealed record RankedGenome(NeuralGenome Genome, double? Fitness, bool Failed);
