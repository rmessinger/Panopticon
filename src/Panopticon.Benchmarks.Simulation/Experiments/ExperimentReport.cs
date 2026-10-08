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
