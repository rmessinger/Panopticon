namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Play-stage seam for the B4a consumer benchmark. Runs <paramref name="frames"/> frames over the
/// map package at <paramref name="packageDirectory"/> and returns the number of frames completed.
/// </summary>
public interface IB4aPlayRuntime
{
    int RunFrames(string packageDirectory, int frames, uint seed);
}
