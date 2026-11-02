using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Features;

namespace Panopticon.Geospatial.Preview;

public sealed record PreviewFeatureKindCount(GeneratedFeatureKind Kind, long Count);

/// <summary>
/// Build report of one preview conversion: quantization and clamp loss, unrepresented
/// payload state, and waypoint conversion counts.
/// </summary>
public sealed class GeneratedMapPreviewReport
{
    public GeneratedMapPreviewReport(
        int cropChunkXLo,
        int cropChunkYLo,
        int cropChunkXHi,
        int cropChunkYHi,
        uint storedWidth,
        uint storedHeight,
        uint borderWidth,
        IReadOnlyList<string> textureNames,
        long impassableCellCount,
        long unrepresentedUnbuildableCellCount,
        long clampedHeightSampleCount,
        double maxQuantizationErrorGameUnits,
        int convertedWaypointCount,
        int excludedWaypointCount,
        IReadOnlyList<int> overriddenPlayerStartSlots,
        IReadOnlyList<PreviewFeatureKindCount> unrepresentedFeatureCounts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cropChunkXLo);
        ArgumentOutOfRangeException.ThrowIfNegative(cropChunkYLo);
        ArgumentOutOfRangeException.ThrowIfLessThan(cropChunkXHi, cropChunkXLo + 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cropChunkYHi, cropChunkYLo + 1);
        ArgumentNullException.ThrowIfNull(textureNames);
        ArgumentNullException.ThrowIfNull(overriddenPlayerStartSlots);
        ArgumentNullException.ThrowIfNull(unrepresentedFeatureCounts);

        CropChunkXLo = cropChunkXLo;
        CropChunkYLo = cropChunkYLo;
        CropChunkXHi = cropChunkXHi;
        CropChunkYHi = cropChunkYHi;
        StoredWidth = storedWidth;
        StoredHeight = storedHeight;
        BorderWidth = borderWidth;
        TextureNames = new ReadOnlyCollection<string>([.. textureNames]);
        ImpassableCellCount = impassableCellCount;
        UnrepresentedUnbuildableCellCount = unrepresentedUnbuildableCellCount;
        ClampedHeightSampleCount = clampedHeightSampleCount;
        MaxQuantizationErrorGameUnits = maxQuantizationErrorGameUnits;
        ConvertedWaypointCount = convertedWaypointCount;
        ExcludedWaypointCount = excludedWaypointCount;
        OverriddenPlayerStartSlots = new ReadOnlyCollection<int>([.. overriddenPlayerStartSlots]);
        UnrepresentedFeatureCounts = new ReadOnlyCollection<PreviewFeatureKindCount>([.. unrepresentedFeatureCounts]);
    }

    public int CropChunkXLo { get; }

    public int CropChunkYLo { get; }

    public int CropChunkXHi { get; }

    public int CropChunkYHi { get; }

    /// <summary>Stored height samples per axis, border padding included.</summary>
    public uint StoredWidth { get; }

    public uint StoredHeight { get; }

    public uint BorderWidth { get; }

    /// <summary>Canonical texture block names in texture-table order.</summary>
    public IReadOnlyList<string> TextureNames { get; }

    /// <summary>Core cells painted impassable from the payload Impassable mask bit.</summary>
    public long ImpassableCellCount { get; }

    /// <summary>
    /// Core cells whose payload Unbuildable bit is set: retained in the payload, not
    /// converted, because the legacy Buildability polarity is unverified (deep dive 6.1).
    /// </summary>
    public long UnrepresentedUnbuildableCellCount { get; }

    /// <summary>Quantized samples clamped to the ushort range.</summary>
    public long ClampedHeightSampleCount { get; }

    /// <summary>Largest restored-Z error over all stored samples, clamp loss included, in game units.</summary>
    public double MaxQuantizationErrorGameUnits { get; }

    /// <summary>Waypoints converted into map objects, player starts included.</summary>
    public int ConvertedWaypointCount { get; }

    /// <summary>Waypoint features excluded because they lie outside the crop lattice extent.</summary>
    public int ExcludedWaypointCount { get; }

    /// <summary>Skirmish slots whose factory default start was overridden (1 and 2).</summary>
    public IReadOnlyList<int> OverriddenPlayerStartSlots { get; }

    /// <summary>Counts of payload features with no legacy representation, by kind.</summary>
    public IReadOnlyList<PreviewFeatureKindCount> UnrepresentedFeatureCounts { get; }
}
