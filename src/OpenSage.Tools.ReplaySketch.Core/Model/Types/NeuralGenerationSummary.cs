#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

public sealed class NeuralGenerationSummary
{
    public double? Best { get; set; }

    public double? Mean { get; set; }

    public double? Worst { get; set; }

    /// <summary>Mean pairwise L2 distance over the generation's weight vectors.</summary>
    public double Diversity { get; set; }
}
