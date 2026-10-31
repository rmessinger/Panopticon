using System;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Per-import terrain report: aggregated <see cref="SamplingStats"/> plus generated chunk
/// count and combined height range, recorded while chunks stream.
/// </summary>
public sealed class TerrainGenerationReport
{
    /// <summary>Aggregated sampling statistics; fill events stay counters here, not diagnostic strings.</summary>
    public SamplingStats Sampling { get; } = new();

    /// <summary>Number of chunks generated so far.</summary>
    public long ChunkCount { get; private set; }

    /// <summary>Lowest chunk height seen so far, aprons included; null before the first chunk.</summary>
    public float? MinHeightGameUnits { get; private set; }

    /// <summary>Highest chunk height seen so far, aprons included; null before the first chunk.</summary>
    public float? MaxHeightGameUnits { get; private set; }

    internal void Record(TerrainChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        ChunkCount++;
        MinHeightGameUnits = MinHeightGameUnits is { } min
            ? Math.Min(min, chunk.MinHeightGameUnits)
            : chunk.MinHeightGameUnits;
        MaxHeightGameUnits = MaxHeightGameUnits is { } max
            ? Math.Max(max, chunk.MaxHeightGameUnits)
            : chunk.MaxHeightGameUnits;
    }
}
