using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// Executes one experiment definition: validates it against the registry, expands the cells,
/// and runs each cell through the B1 runner with the cell's resolved bindings attached to the
/// plan. Benchmark mechanics stay in B1; this class only selects and orchestrates. A cell
/// that throws is recorded as failed and the remaining cells still run.
/// </summary>
public sealed class ExperimentRunner
{
    private readonly ExperimentImplementationRegistry _registry;
    private readonly Func<ExperimentCellPlan, BenchmarkRunRecord> _executeCell;

    public ExperimentRunner(ExperimentImplementationRegistry registry)
        : this(registry, ExecuteBenchmarkCell)
    {
    }

    internal ExperimentRunner(
        ExperimentImplementationRegistry registry,
        Func<ExperimentCellPlan, BenchmarkRunRecord> executeCell)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(executeCell);

        _registry = registry;
        _executeCell = executeCell;
    }

    public ExperimentExecution Run(ExperimentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _registry.ValidateDefinition(definition);

        var results = new List<ExperimentCellResult>();
        foreach (var cell in definition.Expand())
        {
            results.Add(RunCell(definition, cell));
        }

        return new ExperimentExecution(definition, results);
    }

    private ExperimentCellResult RunCell(ExperimentDefinition definition, ExperimentCell cell)
    {
        var bindings = cell.Bindings
            .Select(b =>
            {
                var descriptor = _registry.Resolve(b.Key, b.Value);
                return new ResolvedExperimentBinding(descriptor, descriptor.CreateSettings());
            })
            .ToList();
        var plan = new SimulationBenchmarkPlan(
            definition.ScenarioId,
            definition.Seed,
            definition.WarmupFrames,
            definition.MeasuredFrames)
        {
            ExperimentBindings = bindings,
        };

        try
        {
            var record = _executeCell(new ExperimentCellPlan(definition, cell, bindings, plan));
            return Succeeded(definition, cell, bindings, record);
        }
        catch (Exception ex)
        {
            return new ExperimentCellResult
            {
                CellKey = cell.Key,
                Bindings = [.. bindings.Select(IdentityOf)],
                ScenarioId = definition.ScenarioId,
                Seed = definition.Seed,
                Succeeded = false,
                Error = ex.Message,
            };
        }
    }

    private static ExperimentCellResult Succeeded(
        ExperimentDefinition definition,
        ExperimentCell cell,
        IReadOnlyList<ResolvedExperimentBinding> bindings,
        BenchmarkRunRecord record)
    {
        return new ExperimentCellResult
        {
            CellKey = cell.Key,
            Bindings = [.. bindings.Select(IdentityOf)],
            ScenarioId = definition.ScenarioId,
            Seed = definition.Seed,
            Succeeded = true,
            Record = record,
        };
    }

    private static ExperimentBindingIdentity IdentityOf(ResolvedExperimentBinding binding)
    {
        return new ExperimentBindingIdentity(
            binding.Descriptor.Seam,
            binding.Descriptor.Id,
            binding.Descriptor.Version);
    }

    private static BenchmarkRunRecord ExecuteBenchmarkCell(ExperimentCellPlan cellPlan)
    {
        return new SimulationBenchmarkRunner().Run(cellPlan.Plan);
    }
}

/// <summary>Everything one cell execution needs: the definition, the cell, its resolved
/// bindings, and the constructed B1 plan.</summary>
public sealed record ExperimentCellPlan(
    ExperimentDefinition Definition,
    ExperimentCell Cell,
    IReadOnlyList<ResolvedExperimentBinding> Bindings,
    SimulationBenchmarkPlan Plan);
