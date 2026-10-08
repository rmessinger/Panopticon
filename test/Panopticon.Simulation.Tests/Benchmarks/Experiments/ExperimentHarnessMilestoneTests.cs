using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Benchmarks.Simulation.Experiments;
using Panopticon.Logic.AI;
using Xunit;

namespace Panopticon.Tests.Benchmarks.Experiments;

/// <summary>
/// WI-A4-41 milestone proof: one two-cell experiment selects the rule-based and neural AI
/// policies through configuration only, runs the same B1 scenario, and produces one
/// comparable report with no caller changes. The tests never assert which policy performs
/// better; comparisons only check that both cells were measured and reported.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class ExperimentHarnessMilestoneTests
{
    [Fact]
    public void TwoCellExperiment_RunsSameScenarioAndProducesOneComparableReport()
    {
        var definition = MilestoneDefinition();

        var execution = new ExperimentRunner(ExperimentImplementationRegistry.CreateDefault()).Run(definition);

        Assert.True(execution.AllCellsSucceeded, FormatFailures(execution));
        Assert.Equal(
            new[] { "ai-policy=rule-based", "ai-policy=neural" },
            execution.Cells.Select(cell => cell.CellKey).ToArray());
        Assert.All(
            execution.Cells,
            cell => Assert.Equal(AiPolicyBenchmarkScenario.ScenarioId, cell.ScenarioId));
        Assert.All(
            execution.Cells.SelectMany(cell => cell.Bindings),
            binding => Assert.Equal(AiPolicyExperimentImplementations.SeamKey, binding.Seam));

        var directory = Path.Combine(Path.GetTempPath(), "a4-41-milestone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var report = ExperimentReportWriter.FromJson(File.ReadAllText(reportPath));

            Assert.Equal(ExperimentReportWriter.CurrentSchemaVersion, report.SchemaVersion);
            Assert.Equal(definition.Name, report.ExperimentName);
            Assert.Equal(2, report.Cells.Count);
            Assert.All(report.Cells, cell => Assert.Equal("succeeded", cell.Status));
            Assert.NotEmpty(report.Comparisons);
            Assert.NotNull(report.Environment);

            foreach (var cell in report.Cells)
            {
                Assert.True(
                    File.Exists(ExperimentReportWriter.ResolveArtifact(reportPath, cell.RunArtifact!)),
                    $"Artifact '{cell.RunArtifact}' for cell '{cell.CellKey}' must resolve relative to the report.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(AiPolicyExperimentImplementations.RuleBasedId)]
    [InlineData(AiPolicyExperimentImplementations.NeuralId)]
    public void SelectedBinding_DrivesTheAiSlotPolicyWithoutCallerChanges(string implementationId)
    {
        var descriptor = ExperimentImplementationRegistry.CreateDefault().Resolve(
            AiPolicyExperimentImplementations.SeamKey, implementationId);
        var plan = new SimulationBenchmarkPlan(AiPolicyBenchmarkScenario.ScenarioId, seed: 1, warmupFrames: 1, measuredFrames: 5)
        {
            ExperimentBindings = [new ResolvedExperimentBinding(descriptor, descriptor.CreateSettings())],
        };

        var scenario = SimulationBenchmarkScenario.Create(plan.ScenarioId);
        using var run = scenario.CreateRun(plan);

        // Exactly one booted player keeps its policy host: the AI slot carrying the selected
        // payload; every other slot was disabled during boot.
        var players = run.Game.Scene3D.Players.Where(player => player.AIPlayer != null).ToList();
        Assert.Single(players);
        var skirmishHost = Assert.IsType<SkirmishAIPlayer>(players[0].AIPlayer);
        Assert.IsType(ExpectedPolicyType(implementationId), skirmishHost.DecisionPolicy);
    }

    [Fact]
    public void BenchmarkSources_DoNotBranchOnPolicyType()
    {
        var sourceRoot = FindBenchmarkSourceRoot();
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !Path.GetFileName(path).Equals("AiPolicyExperimentImplementations.cs", StringComparison.Ordinal))
            .Select(path => new { Path = path, Content = File.ReadAllText(path) })
            .Where(file => file.Content.Contains("RuleBasedAIDecisionPolicy")
                || file.Content.Contains("NeuralNetworkAIDecisionPolicy")
                || file.Content.Contains("HierarchicalAIDecisionPolicy"))
            .Select(file => Path.GetFileName(file.Path))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Benchmark sources must not branch on policy types; references found in: {string.Join(", ", offenders)}");
    }

    internal static ExperimentDefinition MilestoneDefinition()
    {
        return new ExperimentDefinition(
            "ai-policy-milestone",
            AiPolicyBenchmarkScenario.ScenarioId,
            seed: 1,
            warmupFrames: 1,
            measuredFrames: 20,
            axes:
            [
                new ExperimentAxis(
                    AiPolicyExperimentImplementations.SeamKey,
                    [AiPolicyExperimentImplementations.RuleBasedId, AiPolicyExperimentImplementations.NeuralId]),
            ]);
    }

    private static Type ExpectedPolicyType(string implementationId)
    {
        return implementationId == AiPolicyExperimentImplementations.NeuralId
            ? typeof(NeuralNetworkAIDecisionPolicy)
            : typeof(RuleBasedAIDecisionPolicy);
    }

    private static string FormatFailures(ExperimentExecution execution)
    {
        var failures = execution.Cells
            .Where(cell => !cell.Succeeded)
            .Select(cell => $"{cell.CellKey}: {cell.Error}");
        return $"All cells must succeed; failures: {string.Join("; ", failures)}";
    }

    private static string FindBenchmarkSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Panopticon.Benchmarks.Simulation");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate src/Panopticon.Benchmarks.Simulation from the test assembly.");
    }
}
