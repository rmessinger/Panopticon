using System;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One macro benchmark execution: the scenario to run, its seed, and how many warmup and
/// measured frames to step. Plan construction never touches a game installation.
/// </summary>
public sealed class SimulationBenchmarkPlan
{
    public SimulationBenchmarkPlan(string scenarioId, uint seed, int warmupFrames, int measuredFrames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredFrames);

        ScenarioId = scenarioId;
        Seed = seed;
        WarmupFrames = warmupFrames;
        MeasuredFrames = measuredFrames;
    }

    public string ScenarioId { get; }

    public uint Seed { get; }

    public int WarmupFrames { get; }

    public int MeasuredFrames { get; }

    /// <summary>Optional map override; the scenario picks its own default when null.</summary>
    public string? MapPath { get; init; }

    /// <summary>Optional JSON report destination; nothing is written when null.</summary>
    public string? JsonPath { get; init; }

    /// <summary>Optional CSV summary destination; accepted now, written from B1-4 on.</summary>
    public string? CsvPath { get; init; }
}
