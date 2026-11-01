using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>Imagery generation summary: sampling snapshot plus chunk count.</summary>
public sealed record PayloadImageryGenerationSummary
{
    public PayloadImageryGenerationSummary(PayloadSamplingSummary sampling, long chunkCount)
    {
        ArgumentNullException.ThrowIfNull(sampling);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        Sampling = sampling;
        ChunkCount = chunkCount;
    }

    /// <summary>Aggregated imagery sampling statistics.</summary>
    public PayloadSamplingSummary Sampling { get; }

    public long ChunkCount { get; }
}
