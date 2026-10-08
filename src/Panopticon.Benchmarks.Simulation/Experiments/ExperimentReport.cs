using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// The A5-consumable experiment report: one entry per cell with binding identities and
/// artifact references, shared-metric comparison rows derived from the per-cell B1
/// statistics, and one hardware context for the experiment. Artifact references are
/// relative to the report file so the directory can be moved or archived whole.
/// </summary>
public sealed class ExperimentReport
{
    public string SchemaVersion { get; set; } = "a4.experiment/1";

    public string ExperimentName { get; set; } = string.Empty;

    public string ScenarioId { get; set; } = string.Empty;

    public uint Seed { get; set; }

    /// <summary>Stable digest of the definition JSON; identifies what ran.</summary>
    public string DefinitionDigest { get; set; } = string.Empty;

    public List<ExperimentReportCell> Cells { get; set; } = [];

    public List<ExperimentMetricComparison> Comparisons { get; set; } = [];

    public EnvironmentRecord? Environment { get; set; }
}

/// <summary>One cell in the report: identity, status, and the relative run artifact.</summary>
public sealed class ExperimentReportCell
{
    public string CellKey { get; set; } = string.Empty;

    public List<ExperimentBindingIdentity> Bindings { get; set; } = [];

    public string ScenarioId { get; set; } = string.Empty;

    public uint Seed { get; set; }

    /// <summary>Either `succeeded` or `failed`; failed cells carry `error` and no artifact.</summary>
    public string Status { get; set; } = "succeeded";

    public string? Error { get; set; }

    public string? RunArtifact { get; set; }
}

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

/// <summary>The per-cell statistics for one compared metric.</summary>
public sealed class ExperimentMetricCell
{
    public string CellKey { get; set; } = string.Empty;

    public int Samples { get; set; }

    public double Median { get; set; }

    public double ConfidenceLow { get; set; }

    public double ConfidenceHigh { get; set; }
}
