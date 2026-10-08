using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

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
