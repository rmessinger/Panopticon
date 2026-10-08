using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Benchmarks.Simulation.Experiments;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A4-40 experiment report: JSON round-trip with statistics and hardware context, relative
/// artifact references that survive moving the directory, machine-readable partial failures,
/// interval-difference comparison rows, and explicit unsupported-version rejection.
/// </summary>
public class ExperimentReportTests
{
    private static ExperimentCellResult Cell(
        string key,
        bool succeeded,
        double low = 1.0,
        double median = 2.0,
        double high = 3.0,
        string? error = null)
    {
        return new ExperimentCellResult
        {
            CellKey = key,
            Bindings = [new ExperimentBindingIdentity("ai-policy", key, "1")],
            ScenarioId = "b1.empty-host",
            Seed = 11,
            Succeeded = succeeded,
            Error = error,
            Record = succeeded ? Record(low, median, high) : null,
        };
    }

    private static BenchmarkRunRecord Record(double low, double median, double high)
    {
        return new BenchmarkRunRecord("b1.empty-host", 11, 2, 20, 20, false, 100.0)
        {
            Environment = new EnvironmentRecord { Cpu = "test-cpu", LogicalProcessors = 8 },
            Statistics =
            [
                new BenchmarkStatistics
                {
                    Metric = "tick.wallMs",
                    Samples = 20,
                    Median = median,
                    ConfidenceLow = low,
                    ConfidenceHigh = high,
                },
            ],
        };
    }

    private static ExperimentExecution Execution(params ExperimentCellResult[] cells)
    {
        var definition = new ExperimentDefinition(
            "policy-comparison",
            "b1.empty-host",
            11,
            2,
            20,
            [new ExperimentAxis("ai-policy", ["rule-based", "neural"])]);
        return new ExperimentExecution(definition, cells);
    }

    [Fact]
    public void Write_RoundTripsBindingsStatisticsAndHardware()
    {
        var execution = Execution(
            Cell("ai-policy=rule-based", succeeded: true),
            Cell("ai-policy=neural", succeeded: true, low: 9.0, median: 10.0, high: 11.0));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var report = ExperimentReportWriter.FromJson(File.ReadAllText(reportPath));

            Assert.Equal("a4.experiment/1", report.SchemaVersion);
            Assert.Equal("policy-comparison", report.ExperimentName);
            Assert.Equal("b1.empty-host", report.ScenarioId);
            Assert.Equal(11u, report.Seed);
            Assert.Equal(2, report.Cells.Count);
            Assert.All(report.Cells, c => Assert.Equal("succeeded", c.Status));
            Assert.Equal(8, report.Environment!.LogicalProcessors);
            var comparison = Assert.Single(report.Comparisons);
            Assert.Equal("tick.wallMs", comparison.Metric);
            Assert.Equal(2.0, comparison.Cells[0].Median);
            Assert.Equal(20, comparison.Cells[0].Samples);
            Assert.Equal(1.0, comparison.Cells[0].ConfidenceLow);
            Assert.Equal(3.0, comparison.Cells[0].ConfidenceHigh);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_NonOverlappingIntervalsFlagAsDiffering()
    {
        var execution = Execution(
            Cell("ai-policy=rule-based", succeeded: true, low: 1.0, median: 2.0, high: 3.0),
            Cell("ai-policy=neural", succeeded: true, low: 9.0, median: 10.0, high: 11.0));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var report = ExperimentReportWriter.FromJson(File.ReadAllText(reportPath));

            var comparison = Assert.Single(report.Comparisons);
            Assert.Equal(["ai-policy=neural|ai-policy=rule-based"], comparison.DifferingPairs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_ArtifactReferencesSurviveMovingTheDirectory()
    {
        var execution = Execution(Cell("ai-policy=rule-based", succeeded: true));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));
        var moved = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var report = ExperimentReportWriter.FromJson(File.ReadAllText(reportPath));
            var artifact = Assert.Single(report.Cells, c => c.Status == "succeeded").RunArtifact;

            Directory.Move(directory, moved);
            var resolved = ExperimentReportWriter.ResolveArtifact(
                Path.Combine(moved, "experiment.json"),
                artifact!);
            Assert.True(File.Exists(resolved));
            Assert.NotNull(BenchmarkReportWriter.FromJson(File.ReadAllText(resolved)));
        }
        finally
        {
            Directory.Delete(moved, recursive: true);
        }
    }

    [Fact]
    public void Write_PartialFailureStaysMachineReadable()
    {
        var execution = Execution(
            Cell("ai-policy=rule-based", succeeded: true),
            Cell("ai-policy=neural", succeeded: false, error: "boom"));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var report = ExperimentReportWriter.FromJson(File.ReadAllText(reportPath));

            var failed = Assert.Single(report.Cells, c => c.Status == "failed");
            Assert.Equal("ai-policy=neural", failed.CellKey);
            Assert.Equal("boom", failed.Error);
            Assert.Null(failed.RunArtifact);
            Assert.NotNull(Assert.Single(report.Cells, c => c.Status == "succeeded").RunArtifact);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FromJson_RejectsUnsupportedVersionAndNonReports()
    {
        var execution = Execution(Cell("ai-policy=rule-based", succeeded: true));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            var reportPath = ExperimentReportWriter.Write(execution, directory);
            var versionError = Assert.Throws<ArgumentException>(() =>
                ExperimentReportWriter.FromJson(
                    File.ReadAllText(reportPath).Replace("a4.experiment/1", "a4.experiment/9")));
            Assert.Contains("not supported", versionError.Message);
            Assert.Contains("a4.experiment/9", versionError.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Assert.Throws<ArgumentException>(() => ExperimentReportWriter.FromJson("{}"));
    }

    [Fact]
    public void Write_DerivedCsvSummaryExists()
    {
        var execution = Execution(Cell("ai-policy=rule-based", succeeded: true));
        var directory = Path.Combine(Path.GetTempPath(), "a4-report", Guid.NewGuid().ToString("N"));

        try
        {
            ExperimentReportWriter.Write(execution, directory);

            var csv = File.ReadAllText(Path.Combine(directory, "experiment.csv"));
            Assert.StartsWith("experimentName,scenarioId,seed,cellKey,metric", csv);
            Assert.Contains("tick.wallMs", csv);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
