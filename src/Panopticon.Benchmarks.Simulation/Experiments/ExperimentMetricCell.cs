namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>The per-cell statistics for one compared metric.</summary>
public sealed class ExperimentMetricCell
{
    public string CellKey { get; set; } = string.Empty;

    public int Samples { get; set; }

    public double Median { get; set; }

    public double ConfidenceLow { get; set; }

    public double ConfidenceHigh { get; set; }
}
