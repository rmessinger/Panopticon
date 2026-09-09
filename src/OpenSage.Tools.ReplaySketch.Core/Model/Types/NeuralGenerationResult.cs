#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One generation's outcome: its report and the advanced population.</summary>
public sealed record NeuralGenerationResult(
    NeuralGenerationReport Report,
    NeuralPopulation NextPopulation);
