using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// Persists experiment executions: the report JSON is the authority, the per-cell B1 records
/// are written beside it and referenced relatively, and the CSV summary is derived from the
/// report. Loading rejects unsupported schema versions explicitly.
/// </summary>
public static class ExperimentReportWriter
{
    public const string CurrentSchemaVersion = "a4.experiment/1";

    private const string ReportFileName = "experiment.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(ExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static ExperimentReport FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var report = JsonSerializer.Deserialize<ExperimentReport>(json, JsonOptions);
            if (report is null || string.IsNullOrWhiteSpace(report.ExperimentName))
            {
                throw new ArgumentException("The JSON does not contain an experiment report.");
            }

            if (report.SchemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentException(
                    $"The experiment report schema version '{report.SchemaVersion}' is not supported; expected '{CurrentSchemaVersion}'.");
            }

            return report;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The JSON does not contain an experiment report.", ex);
        }
    }

    /// <summary>
    /// Writes the artifact directory for one execution:
    /// `experiment.json` plus `cells/&lt;cellKey&gt;/run.json` and `run.csv` per succeeded
    /// cell. Returns the report path.
    /// </summary>
    public static string Write(ExperimentExecution execution, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        var report = BuildReport(execution, outputDirectory);
        var reportPath = Path.Combine(outputDirectory, ReportFileName);
        File.WriteAllText(reportPath, ToJson(report));
        WriteCsv(report, Path.Combine(outputDirectory, "experiment.csv"));
        return reportPath;
    }

    /// <summary>Resolves a report artifact reference against the directory the report lives
    /// in; works wherever the directory is moved.</summary>
    public static string ResolveArtifact(string reportPath, string relativeArtifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeArtifact);

        return Path.Combine(Path.GetDirectoryName(reportPath) ?? ".", relativeArtifact);
    }

    private static ExperimentReport BuildReport(ExperimentExecution execution, string outputDirectory)
    {
        var succeeded = execution.Cells.Where(c => c.Succeeded && c.Record is not null).ToList();
        var report = new ExperimentReport
        {
            ExperimentName = execution.Definition.Name,
            ScenarioId = execution.Definition.ScenarioId,
            Seed = execution.Definition.Seed,
            DefinitionDigest = DigestOf(execution.Definition),
            Environment = succeeded.Select(c => c.Record!.Environment).FirstOrDefault(),
        };

        foreach (var cell in execution.Cells)
        {
            var entry = new ExperimentReportCell
            {
                CellKey = cell.CellKey,
                Bindings = [.. cell.Bindings],
                ScenarioId = cell.ScenarioId,
                Seed = cell.Seed,
                Status = cell.Succeeded ? "succeeded" : "failed",
                Error = cell.Error,
            };

            if (cell.Succeeded && cell.Record is not null)
            {
                var cellDirectory = Path.Combine(outputDirectory, "cells", cell.CellKey);
                Directory.CreateDirectory(cellDirectory);
                BenchmarkReportWriter.WriteJson(cell.Record, Path.Combine(cellDirectory, "run.json"));
                BenchmarkReportWriter.WriteCsv(cell.Record, Path.Combine(cellDirectory, "run.csv"));
                entry.RunArtifact = $"cells/{cell.CellKey}/run.json";
            }

            report.Cells.Add(entry);
        }

        report.Comparisons.AddRange(BuildComparisons(execution));
        return report;
    }

    private static IEnumerable<ExperimentMetricComparison> BuildComparisons(ExperimentExecution execution)
    {
        var statsByCell = execution.Cells
            .Where(c => c.Succeeded && c.Record is not null)
            .ToDictionary(c => c.CellKey, c => c.Record!.Statistics);
        foreach (var metric in statsByCell.Values.SelectMany(s => s).Select(s => s.Metric).Distinct().OrderBy(m => m))
        {
            var comparison = new ExperimentMetricComparison { Metric = metric };
            var byCell = new Dictionary<string, BenchmarkStatistics>();
            foreach (var (cellKey, statistics) in statsByCell)
            {
                var statistic = statistics.FirstOrDefault(s => s.Metric == metric);
                if (statistic is null)
                {
                    continue;
                }

                byCell[cellKey] = statistic;
                comparison.Cells.Add(new ExperimentMetricCell
                {
                    CellKey = cellKey,
                    Samples = statistic.Samples,
                    Median = statistic.Median,
                    ConfidenceLow = statistic.ConfidenceLow,
                    ConfidenceHigh = statistic.ConfidenceHigh,
                });
            }

            comparison.DifferingPairs.AddRange(DifferingPairs(byCell));
            yield return comparison;
        }
    }

    private static IEnumerable<string> DifferingPairs(Dictionary<string, BenchmarkStatistics> byCell)
    {
        var keys = byCell.Keys.OrderBy(k => k).ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = i + 1; j < keys.Count; j++)
            {
                if (BenchmarkStatistics.IntervalsDiffer(byCell[keys[i]], byCell[keys[j]]))
                {
                    yield return $"{keys[i]}|{keys[j]}";
                }
            }
        }
    }

    private static string DigestOf(ExperimentDefinition definition)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(definition.ToJson()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteCsv(ExperimentReport report, string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("experimentName,scenarioId,seed,cellKey,metric,samples,median,ciLow,ciHigh,status");
        foreach (var cell in report.Cells)
        {
            foreach (var comparison in report.Comparisons)
            {
                var metricCell = comparison.Cells.FirstOrDefault(c => c.CellKey == cell.CellKey);
                if (metricCell is null)
                {
                    continue;
                }

                builder.AppendLine(string.Join(',',
                    report.ExperimentName,
                    report.ScenarioId,
                    report.Seed,
                    cell.CellKey,
                    comparison.Metric,
                    metricCell.Samples,
                    metricCell.Median.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    metricCell.ConfidenceLow.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    metricCell.ConfidenceHigh.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    cell.Status));
            }
        }

        File.WriteAllText(path, builder.ToString());
    }
}
