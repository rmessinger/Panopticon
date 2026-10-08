using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One executed experiment cell: its stable key, binding identities, the scenario and seed it
/// ran, the B1 record when it succeeded, and the error when it failed. A failed cell never
/// erases the results of other cells.
/// </summary>
public sealed class ExperimentCellResult
{
    public string CellKey { get; init; } = string.Empty;

    public IReadOnlyList<ExperimentBindingIdentity> Bindings { get; init; } = [];

    public string ScenarioId { get; init; } = string.Empty;

    public uint Seed { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }

    /// <summary>The per-cell B1 record with samples, statistics, and hardware context.</summary>
    public BenchmarkRunRecord? Record { get; init; }
}
