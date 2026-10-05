namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One tick observation: wall time, allocation delta, cumulative GC counts, and, at the
/// configured memory interval, heap and working-set samples.
/// </summary>
public sealed class TickMetricSample
{
    public TickMetricSample(uint frame)
    {
        Frame = frame;
    }

    public uint Frame { get; }

    public double WallMs { get; set; }

    public long AllocBytes { get; set; }

    public int Gc0 { get; set; }

    public int Gc1 { get; set; }

    public int Gc2 { get; set; }

    public long? HeapBytes { get; set; }

    public long? WorkingSetBytes { get; set; }
}
