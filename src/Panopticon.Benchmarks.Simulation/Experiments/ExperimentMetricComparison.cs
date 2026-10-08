using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One shared metric compared across cells: per-cell median statistics from the B1 records
/// and the pairs whose confidence intervals do not overlap
/// (`BenchmarkStatistics.IntervalsDiffer`).
/// </summary>
public sealed class ExperimentMetricComparison
{
    public string Metric { get; set; } = string.Empty;

    public List<ExperimentMetricCell> Cells { get; set; } = [];

    public List<string> DifferingPairs { get; set; } = [];
}
