using System;
using System.Collections.Generic;
using System.Linq;

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

/// <summary>Stable identity of one selected implementation as recorded with every sample.</summary>
public sealed record ExperimentBindingIdentity(string Seam, string Id, string Version);

/// <summary>
/// The outcome of one experiment run: the definition and one result per expanded cell, in
/// expansion order.
/// </summary>
public sealed class ExperimentExecution
{
    public ExperimentExecution(ExperimentDefinition definition, IReadOnlyList<ExperimentCellResult> cells)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(cells);

        Definition = definition;
        Cells = cells;
    }

    public ExperimentDefinition Definition { get; }

    public IReadOnlyList<ExperimentCellResult> Cells { get; }

    public bool AllCellsSucceeded => Cells.All(c => c.Succeeded);
}
