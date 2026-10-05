using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Persists benchmark run records: the JSON record is the authority (A2-compatible camelCase
/// shape extending the A1 run output); the CSV summary is derived from it, one row per metric
/// aggregate with run and environment columns. Formal bootstrap statistics arrive in B1-5 and
/// will extend both outputs.
/// </summary>
public static class BenchmarkReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(BenchmarkRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return JsonSerializer.Serialize(record, JsonOptions);
    }

    public static BenchmarkRunRecord FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            // System.Text.Json accepts `{}` by defaulting constructor parameters, so a
            // scenario id check distinguishes a run record from arbitrary JSON objects.
            var record = JsonSerializer.Deserialize<BenchmarkRunRecord>(json, JsonOptions);
            if (record is null || string.IsNullOrWhiteSpace(record.ScenarioId))
            {
                throw new ArgumentException("The JSON does not contain a benchmark run record.");
            }

            return record;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The JSON does not contain a benchmark run record.", ex);
        }
    }

    public static void WriteJson(BenchmarkRunRecord record, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, ToJson(record));
    }

    public static void WriteCsv(BenchmarkRunRecord record, string path)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, BuildCsv(record), new UTF8Encoding(false));
    }

    private static string BuildCsv(BenchmarkRunRecord record)
    {
        var rows = new List<string>
        {
            "scenarioId,seed,warmupFrames,measuredFrames,framesCompleted,measuredWallMs,os,cpu,logicalProcessors,gcMode,metric,value",
        };

        var prefix = string.Join(
            ',',
            Escape(record.ScenarioId),
            record.Seed.ToString(CultureInfo.InvariantCulture),
            record.WarmupFrames.ToString(CultureInfo.InvariantCulture),
            record.MeasuredFrames.ToString(CultureInfo.InvariantCulture),
            record.FramesCompleted.ToString(CultureInfo.InvariantCulture),
            record.MeasuredWallMs.ToString("R", CultureInfo.InvariantCulture),
            Escape(record.Environment?.Os),
            Escape(record.Environment?.Cpu),
            record.Environment?.LogicalProcessors.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(record.Environment?.GcMode));

        rows.AddRange(RunMetrics(record).Select(m => $"{prefix},{m.metric},{m.value}"));

        return string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    private static IEnumerable<(string metric, string value)> RunMetrics(BenchmarkRunRecord record)
    {
        yield return ("run.wallMs", Format(record.MeasuredWallMs));

        var walls = record.TickSamples.Select(t => t.WallMs).ToArray();
        if (walls.Length > 0)
        {
            yield return ("tick.wallMs.min", Format(walls.Min()));
            yield return ("tick.wallMs.mean", Format(walls.Average()));
            yield return ("tick.wallMs.max", Format(walls.Max()));
        }

        if (record.TickSamples.Count > 0)
        {
            yield return ("tick.allocBytes.total", Format(record.TickSamples.Sum(t => t.AllocBytes)));
            yield return ("tick.gc0.final", Format(record.TickSamples[^1].Gc0));
            yield return ("tick.gc1.final", Format(record.TickSamples[^1].Gc1));
            yield return ("tick.gc2.final", Format(record.TickSamples[^1].Gc2));
            yield return ("tick.count", Format(record.TickSamples.Count));
        }

        foreach (var group in record.SystemSamples.GroupBy(s => s.System).OrderBy(g => g.Key))
        {
            yield return ($"system.{group.Key}.meanMs", Format(group.Average(s => s.SpanMs)));
            yield return ($"system.{group.Key}.maxMs", Format(group.Max(s => s.SpanMs)));
            yield return ($"system.{group.Key}.count", Format(group.Count()));
        }

        foreach (var stat in record.Statistics)
        {
            yield return ($"stat.{stat.Metric}.median", Format(stat.Median));
            yield return ($"stat.{stat.Metric}.ciLow", Format(stat.ConfidenceLow));
            yield return ($"stat.{stat.Metric}.ciHigh", Format(stat.ConfidenceHigh));
            if (stat.IsRegression.HasValue)
            {
                yield return ($"stat.{stat.Metric}.regression", stat.IsRegression.Value ? "1" : "0");
            }
        }
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;

        return value.Contains(',', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
