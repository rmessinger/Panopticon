using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation.Experiments;

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
