#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>Per-term mean fitness across a genome's successful matches.</summary>
public sealed class NeuralFitnessTermsReport
{
    public double Outcome { get; set; }

    public double Economy { get; set; }

    public double Army { get; set; }

    public double Speed { get; set; }
}
