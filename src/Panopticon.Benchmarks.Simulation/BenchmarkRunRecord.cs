namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Skeleton B1 run record. B1-3 appends tick and system samples plus the environment record,
/// B1-4 owns the A2-compatible JSON writer and CSV summary, B1-5 appends statistics.
/// </summary>
public sealed class BenchmarkRunRecord
{
    public BenchmarkRunRecord(
        string scenarioId,
        uint seed,
        int warmupFrames,
        int measuredFrames,
        int framesCompleted,
        bool stopPredicateTriggered,
        double measuredWallMs)
    {
        ScenarioId = scenarioId;
        Seed = seed;
        WarmupFrames = warmupFrames;
        MeasuredFrames = measuredFrames;
        FramesCompleted = framesCompleted;
        StopPredicateTriggered = stopPredicateTriggered;
        MeasuredWallMs = measuredWallMs;
    }

    public string SchemaVersion { get; } = "b1.run/1";

    public string ScenarioId { get; }

    public uint Seed { get; }

    public int WarmupFrames { get; }

    public int MeasuredFrames { get; }

    public int FramesCompleted { get; }

    public bool StopPredicateTriggered { get; }

    public double MeasuredWallMs { get; }

    public RunOutput? Output { get; init; }
}
