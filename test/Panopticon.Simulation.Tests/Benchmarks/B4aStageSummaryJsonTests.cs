using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests.Benchmarks;

public sealed class B4aStageSummaryJsonTests
{
    private static readonly string[] Stages = ["b4a.author", "b4a.regionEdit", "b4a.save", "b4a.load", "b4a.play"];
    private static readonly string[] JsonStages = [.. Stages, "b4a.cleanup"];
    private static readonly double[] PlayDisposeMs = [12.0, 14.0, 16.0];

    [Fact]
    public void Serialize_RecordsScenarioSeedRepeatsAndStageOrder()
    {
        var run = RunSmoke();
        using var document = JsonDocument.Parse(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("b4a.consumer", root.GetProperty("scenarioId").GetString());
        Assert.Equal(1L, root.GetProperty("seed").GetInt64());
        Assert.Equal(3, root.GetProperty("repeatCount").GetInt32());
        Assert.Equal(3, root.GetProperty("repeats").GetArrayLength());
        Assert.Equal(JsonStages, root.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
    }

    [Fact]
    public void Serialize_SplitsPlayDisposeIntoCleanupStageWithSampleVariance()
    {
        var run = RunSmoke();
        using var document = JsonDocument.Parse(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs));
        var stages = document.RootElement.GetProperty("stages").EnumerateArray().ToArray();
        var cleanup = stages[^1];
        var play = stages[Array.FindIndex(stages, s => s.GetProperty("stage").GetString() == "b4a.play")];

        Assert.Equal("b4a.cleanup", cleanup.GetProperty("stage").GetString());
        Assert.Equal(3, cleanup.GetProperty("samples").GetInt32());
        AssertClose(14.0, cleanup.GetProperty("wallMs").GetProperty("mean").GetDouble());
        AssertClose(4.0, cleanup.GetProperty("wallMs").GetProperty("sampleVariance").GetDouble());
        AssertClose(Mean(run.Record.TickSamples.Where((_, i) => i % Stages.Length == 4).Select(t => t.WallMs).ToArray()),
            play.GetProperty("wallMs").GetProperty("mean").GetDouble());
    }

    [Fact]
    public void Serialize_RecordsPlanDimensionsFromSmokePlan()
    {
        var run = RunSmoke();
        using var document = JsonDocument.Parse(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs));
        var plan = document.RootElement.GetProperty("plan");

        Assert.Equal(B4aConsumerPlan.SmokeScale.ObjectCount, plan.GetProperty("objectCount").GetInt32());
        Assert.Equal(B4aConsumerPlan.SmokeScale.PlayableSize, plan.GetProperty("playableSize").GetInt32());
        Assert.Equal(B4aConsumerPlan.SmokeScale.LatticeSize, plan.GetProperty("latticeSize").GetInt32());
        Assert.Equal(B4aConsumerPlan.SmokeScale.PlayFrames, plan.GetProperty("playFrames").GetInt32());
        Assert.Equal(B4aConsumerPlan.SmokeScale.Repeats, plan.GetProperty("repeats").GetInt32());
    }

    [Fact]
    public void Serialize_StageMeansAndSampleVariancesMatchTickSamples()
    {
        var run = RunSmoke();
        using var document = JsonDocument.Parse(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs));
        var stages = document.RootElement.GetProperty("stages").EnumerateArray().ToArray();
        var ticks = run.Record.TickSamples;

        Assert.Equal(3 * Stages.Length, ticks.Count);
        for (var index = 0; index < Stages.Length; index++)
        {
            var stageTicks = ticks.Where((_, i) => i % Stages.Length == index).ToArray();
            var wall = stageTicks.Select(t => t.WallMs).ToArray();
            var alloc = stageTicks.Select(t => (double)t.AllocBytes).ToArray();
            var json = stages[index];

            Assert.Equal(Stages[index], json.GetProperty("stage").GetString());
            Assert.Equal(3, json.GetProperty("samples").GetInt32());
            AssertClose(Mean(wall), json.GetProperty("wallMs").GetProperty("mean").GetDouble());
            AssertClose(SampleVariance(wall), json.GetProperty("wallMs").GetProperty("sampleVariance").GetDouble());
            AssertClose(Mean(alloc), json.GetProperty("allocBytesCurrentThread").GetProperty("mean").GetDouble());
            AssertClose(SampleVariance(alloc), json.GetProperty("allocBytesCurrentThread").GetProperty("sampleVariance").GetDouble());
        }
    }

    [Fact]
    public void Serialize_ReportsAllocationAndProcessScopesAndPassingVerification()
    {
        var run = RunSmoke();
        using var document = JsonDocument.Parse(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs));
        var root = document.RootElement;

        Assert.Contains("current thread", root.GetProperty("allocBytesScope").GetString());
        Assert.Contains("process-wide", root.GetProperty("gcScope").GetString());
        Assert.Contains("process", root.GetProperty("processPeakWorkingSet").GetProperty("scope").GetString());

        var verification = root.GetProperty("verification").EnumerateObject().ToArray();
        Assert.Equal(11, verification.Length);
        foreach (var field in verification)
        {
            Assert.True(field.Value.GetBoolean(), field.Name);
        }
    }

    [Fact]
    public void WriteTo_WritesSameTextAsSerialize()
    {
        var run = RunSmoke();
        var path = Path.Combine(Path.GetTempPath(), $"b4a-stage-summary-{Guid.NewGuid():N}.json");
        try
        {
            B4aStageSummaryJson.WriteTo(run, B4aConsumerPlan.SmokeScale, path, PlayDisposeMs);

            Assert.Equal(B4aStageSummaryJson.Serialize(run, B4aConsumerPlan.SmokeScale, PlayDisposeMs), File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static B4aConsumerRun RunSmoke()
    {
        return B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());
    }

    private static double Mean(double[] values)
    {
        return values.Sum() / values.Length;
    }

    private static double SampleVariance(double[] values)
    {
        var mean = Mean(values);
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.True(
            Math.Abs(expected - actual) <= 1e-9 * Math.Max(1.0, Math.Abs(expected)),
            $"expected {expected}, actual {actual}");
    }
}
