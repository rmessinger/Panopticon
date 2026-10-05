using System;
using System.IO;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-4 report writers: JSON round-trips with scenario id, seed, and metric values intact,
/// and the derived CSV carries run plus environment context columns.
/// </summary>
public class BenchmarkReportWriterTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "b1-report-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Json_RoundTripsRecordValues()
    {
        var record = BuildRecord();

        var roundTripped = BenchmarkReportWriter.FromJson(BenchmarkReportWriter.ToJson(record));

        Assert.Equal(record.ScenarioId, roundTripped.ScenarioId);
        Assert.Equal(record.Seed, roundTripped.Seed);
        Assert.Equal(record.WarmupFrames, roundTripped.WarmupFrames);
        Assert.Equal(record.MeasuredFrames, roundTripped.MeasuredFrames);
        Assert.Equal(record.FramesCompleted, roundTripped.FramesCompleted);
        Assert.Equal(record.MeasuredWallMs, roundTripped.MeasuredWallMs);
        Assert.Equal(record.TickSamples.Count, roundTripped.TickSamples.Count);
        Assert.Equal(record.TickSamples[0].WallMs, roundTripped.TickSamples[0].WallMs);
        Assert.Equal(record.TickSamples[0].AllocBytes, roundTripped.TickSamples[0].AllocBytes);
        Assert.Equal(record.SystemSamples[0].System, roundTripped.SystemSamples[0].System);
        Assert.Equal(record.SystemSamples[0].SpanMs, roundTripped.SystemSamples[0].SpanMs);
        Assert.Equal(record.Environment!.Cpu, roundTripped.Environment!.Cpu);
    }

    [Fact]
    public void Json_RoundTripIsStable()
    {
        var record = BuildRecord();

        var once = BenchmarkReportWriter.ToJson(record);
        var twice = BenchmarkReportWriter.ToJson(BenchmarkReportWriter.FromJson(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Json_UsesCamelCaseFields()
    {
        var json = BenchmarkReportWriter.ToJson(BuildRecord());

        Assert.Contains("\"scenarioId\"", json);
        Assert.Contains("\"tickSamples\"", json);
        Assert.Contains("\"systemSamples\"", json);
        Assert.Contains("\"environment\"", json);
    }

    [Fact]
    public void Csv_HasHeaderRunColumnsAndMetricRows()
    {
        var record = BuildRecord();
        var path = Path.Combine(TempDirectory, "report.csv");

        BenchmarkReportWriter.WriteCsv(record, path);

        var lines = File.ReadAllLines(path);
        Assert.StartsWith(
            "scenarioId,seed,warmupFrames,measuredFrames,framesCompleted,measuredWallMs,os,cpu,logicalProcessors,gcMode,metric,value",
            lines[0]);
        Assert.Equal("b1.empty-host", lines[1].Split(',')[0]);
        Assert.Equal("9", lines[1].Split(',')[1]);
        Assert.Contains("tick.wallMs.mean", lines.Select(l => l.Split(',')[^2]));
        Assert.Contains("system.gameLogic.meanMs", lines.Select(l => l.Split(',')[^2]));
    }

    [Fact]
    public void WriteFiles_CreateParentFilesOnDisk()
    {
        var record = BuildRecord();
        var jsonPath = Path.Combine(TempDirectory, "report.json");
        var csvPath = Path.Combine(TempDirectory, "report.csv");

        BenchmarkReportWriter.WriteJson(record, jsonPath);
        BenchmarkReportWriter.WriteCsv(record, csvPath);

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(csvPath));
        Assert.Equal(record.ScenarioId, BenchmarkReportWriter.FromJson(File.ReadAllText(jsonPath)).ScenarioId);
    }

    [Fact]
    public void FromJson_RejectsNonRecordJson()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkReportWriter.FromJson("{}"));
        Assert.Throws<ArgumentException>(() => BenchmarkReportWriter.FromJson("not json"));
    }

    private string TempDirectory
    {
        get
        {
            Directory.CreateDirectory(_tempDirectory);
            return _tempDirectory;
        }
    }

    private static BenchmarkRunRecord BuildRecord()
    {
        return new BenchmarkRunRecord("b1.empty-host", 9, 10, 50, 50, false, 123.456)
        {
            Output = null,
            TickSamples =
            [
                new TickMetricSample(11) { WallMs = 2.5, AllocBytes = 1000, Gc0 = 3, Gc1 = 1, Gc2 = 0 },
                new TickMetricSample(12) { WallMs = 3.5, AllocBytes = 2000, Gc0 = 4, Gc1 = 1, Gc2 = 0 },
            ],
            SystemSamples =
            [
                new SystemMetricSample(11, "gameLogic", 2.1),
                new SystemMetricSample(11, "scene3d", 0.05),
            ],
            Environment = new EnvironmentRecord
            {
                Os = "Test OS",
                Runtime = "10.0",
                GcMode = "Workstation/Interactive",
                Arch = "X64",
                Cpu = "Test CPU",
                LogicalProcessors = 8,
            },
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
