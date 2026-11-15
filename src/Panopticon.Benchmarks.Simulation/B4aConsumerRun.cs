using System.Collections.Generic;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Result of one B4a consumer run. <see cref="Record"/> is the standard B1 run record; the stage
/// summaries and observations are in-memory additions.
/// </summary>
/// <param name="ProcessPeakWorkingSetBytes">
/// Process-lifetime high-water mark (<c>Process.PeakWorkingSet64</c>) read once at record build.
/// Not a per-stage peak.
/// </param>
public sealed record B4aConsumerRun(
    BenchmarkRunRecord Record,
    IReadOnlyList<B4aStageSummary> StageSummaries,
    IReadOnlyList<B4aRepeatObservation> Observations,
    long ProcessPeakWorkingSetBytes);
