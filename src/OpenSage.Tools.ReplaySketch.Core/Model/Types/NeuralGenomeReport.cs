#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One genome's aggregated result in a generation report.</summary>
public sealed class NeuralGenomeReport
{
    public int Index { get; set; }

    /// <summary>1-based rank among successful genomes (fitness descending, ties by lower
    /// index); null for failed genomes, which follow all ranked ones.</summary>
    public int? Rank { get; set; }

    public double? Fitness { get; set; }

    public bool Failed { get; set; }

    public NeuralFitnessTermsReport Terms { get; set; } = new();

    public List<NeuralMatchReport> Matches { get; set; } = new();
}
