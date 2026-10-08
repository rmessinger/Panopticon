using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Benchmarks.Simulation.Experiments;
using Panopticon.Logic.AI;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A4-39 experiment execution with a fake cell executor: cell counts and keys, binding
/// isolation and settings freshness, per-cell identity and hardware context, and explicit
/// per-cell failures that preserve successful cells.
/// </summary>
public class ExperimentRunnerTests
{
    private static ExperimentDefinition PolicyDefinition(
        IReadOnlyList<ExperimentAxis>? axes = null,
        string scenarioId = "b1.empty-host")
    {
        return new ExperimentDefinition(
            "policy-comparison",
            scenarioId,
            11,
            2,
            20,
            axes ?? [new ExperimentAxis("ai-policy", ["rule-based", "neural"])]);
    }

    private sealed class FakeExecutor
    {
        public readonly List<ExperimentCellPlan> Calls = [];

        public Func<ExperimentCellPlan, BenchmarkRunRecord> Record =>
            cellPlan =>
            {
                Calls.Add(cellPlan);
                return NewRecord(cellPlan);
            };

        public Func<ExperimentCellPlan, BenchmarkRunRecord> FailFor(string cellKey) =>
            cellPlan =>
            {
                Calls.Add(cellPlan);
                if (cellPlan.Cell.Key == cellKey)
                {
                    throw new InvalidOperationException("boom");
                }

                return NewRecord(cellPlan);
            };

        private static BenchmarkRunRecord NewRecord(ExperimentCellPlan cellPlan)
        {
            return new BenchmarkRunRecord(
                cellPlan.Definition.ScenarioId,
                cellPlan.Definition.Seed,
                cellPlan.Definition.WarmupFrames,
                cellPlan.Definition.MeasuredFrames,
                cellPlan.Definition.MeasuredFrames,
                stopPredicateTriggered: false,
                measuredWallMs: 1.0)
            {
                Environment = new EnvironmentRecord { Cpu = "fake-cpu" },
            };
        }
    }

    [Fact]
    public void Run_ExecutesOneCellPerCartesianCombination()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.Record);
        var definition = PolicyDefinition(axes:
        [
            new ExperimentAxis("ai-policy", ["rule-based", "neural"]),
            new ExperimentAxis("model-import", ["w3d", "gltf"]),
        ]);

        var execution = runner.Run(definition);

        Assert.Equal(4, execution.Cells.Count);
        Assert.Equal(4, fake.Calls.Count);
        Assert.Equal(
            ["ai-policy=neural;model-import=gltf", "ai-policy=neural;model-import=w3d",
             "ai-policy=rule-based;model-import=gltf", "ai-policy=rule-based;model-import=w3d"],
            execution.Cells.Select(c => c.CellKey).OrderBy(k => k).ToArray());
    }

    [Fact]
    public void Run_IsolatesBindingsPerCell()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.Record);

        var execution = runner.Run(PolicyDefinition());

        var ruleBasedPlan = fake.Calls.Single(p => p.Cell.Key == "ai-policy=rule-based").Plan;
        var neuralPlan = fake.Calls.Single(p => p.Cell.Key == "ai-policy=neural").Plan;
        var ruleBasedPayload = (AIDecisionPolicyPayload)ruleBasedPlan.ExperimentBindings!.Single().Settings;
        var neuralPayload = (AIDecisionPolicyPayload)neuralPlan.ExperimentBindings!.Single().Settings;
        Assert.Equal(AIDecisionPolicyType.RuleBased, ruleBasedPayload.Type);
        Assert.Equal(AIDecisionPolicyType.NeuralNetwork, neuralPayload.Type);
        Assert.Equal(NeuralNetworkAIDecisionPolicy.WeightCount, neuralPayload.NeuralNetworkWeights!.Length);
        Assert.NotSame(ruleBasedPayload, neuralPayload);
    }

    [Fact]
    public void Run_EveryCellCarriesIdentityScenarioSeedAndHardware()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.Record);

        var execution = runner.Run(PolicyDefinition());

        Assert.True(execution.AllCellsSucceeded);
        foreach (var cell in execution.Cells)
        {
            Assert.Equal("b1.empty-host", cell.ScenarioId);
            Assert.Equal(11u, cell.Seed);
            var identity = cell.Bindings.Single();
            Assert.Equal("ai-policy", identity.Seam);
            Assert.Equal(cell.CellKey.Substring("ai-policy=".Length), identity.Id);
            Assert.False(string.IsNullOrWhiteSpace(identity.Version));
            Assert.NotNull(cell.Record);
            Assert.Equal("fake-cpu", cell.Record!.Environment!.Cpu);
            Assert.Equal(11u, cell.Record.Seed);
        }
    }

    [Fact]
    public void Run_FailedCellIsExplicitAndKeepsSuccessfulCells()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.FailFor("ai-policy=neural"));

        var execution = runner.Run(PolicyDefinition());

        Assert.False(execution.AllCellsSucceeded);
        var failed = execution.Cells.Single(c => !c.Succeeded);
        Assert.Equal("ai-policy=neural", failed.CellKey);
        Assert.Equal("boom", failed.Error);
        Assert.Null(failed.Record);
        var succeeded = execution.Cells.Single(c => c.Succeeded);
        Assert.Equal("ai-policy=rule-based", succeeded.CellKey);
        Assert.NotNull(succeeded.Record);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public void Run_InvalidDefinitionFailsBeforeAnyCell()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.Record);
        var definition = PolicyDefinition(axes: [new ExperimentAxis("ai-policy", ["hierarchical"])]);

        Assert.Throws<ArgumentException>(() => runner.Run(definition));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void Run_PlainPlanBindingsDoNotLeakIntoB1Defaults()
    {
        var fake = new FakeExecutor();
        var runner = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault(), fake.Record);

        runner.Run(PolicyDefinition());

        Assert.All(fake.Calls, call => Assert.Single(call.Plan.ExperimentBindings!));
        Assert.Null(new SimulationBenchmarkPlan("b1.empty-host", 1, 1, 1).ExperimentBindings);
    }
}
