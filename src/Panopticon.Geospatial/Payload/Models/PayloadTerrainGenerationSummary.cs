using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>Terrain generation summary: sampling snapshot plus chunk count and height range.</summary>
public sealed record PayloadTerrainGenerationSummary
{
    public PayloadTerrainGenerationSummary(
        PayloadSamplingSummary sampling,
        long chunkCount,
        float? minHeightGameUnits = null,
        float? maxHeightGameUnits = null)
    {
        ArgumentNullException.ThrowIfNull(sampling);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);
        ValidateHeight(minHeightGameUnits, nameof(minHeightGameUnits));
        ValidateHeight(maxHeightGameUnits, nameof(maxHeightGameUnits));

        if (minHeightGameUnits is { } low && maxHeightGameUnits is { } high && low > high)
        {
            throw new ArgumentOutOfRangeException(nameof(minHeightGameUnits), low, "The height range must be ordered.");
        }

        Sampling = sampling;
        ChunkCount = chunkCount;
        MinHeightGameUnits = minHeightGameUnits;
        MaxHeightGameUnits = maxHeightGameUnits;
    }

    /// <summary>Aggregated terrain sampling statistics.</summary>
    public PayloadSamplingSummary Sampling { get; }

    public long ChunkCount { get; }

    /// <summary>Lowest generated chunk height, aprons included; null before the first chunk.</summary>
    public float? MinHeightGameUnits { get; }

    /// <summary>Highest generated chunk height, aprons included; null before the first chunk.</summary>
    public float? MaxHeightGameUnits { get; }

    private static void ValidateHeight(float? height, string paramName)
    {
        if (height is { } value && !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Heights must be finite.");
        }
    }
}
