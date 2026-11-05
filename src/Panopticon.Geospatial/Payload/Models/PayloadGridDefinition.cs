using System;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Grid section of the payload metadata: chunk counts over the fixed
/// <see cref="ChunkLayout"/> lattice, retained transform, projected playable-area
/// polygon with holes, optional imagery resolution, and mask slope thresholds.
/// </summary>
public sealed record PayloadGridDefinition
{
    /// <summary>Conservative cap that keeps global lattice indices inside int range.</summary>
    public const int MaxChunksPerAxis = int.MaxValue / ChunkLayout.PointsPerChunkAxis;

    public const int MaxImageryPixelsPerSample = int.MaxValue / ChunkLayout.IntervalsPerChunk;

    public PayloadGridDefinition(
        int chunksX,
        int chunksY,
        GeospatialMapTransform transform,
        GeoPolygon playableArea,
        int? imageryPixelsPerSample,
        SlopeThresholds slopeThresholds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunksX, MaxChunksPerAxis);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunksY, MaxChunksPerAxis);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(playableArea);
        ArgumentNullException.ThrowIfNull(slopeThresholds);

        PayloadValidationRules.ProjectedGameGridCoordinateSystem(
            transform.ProjectedCoordinateSystem,
            nameof(transform));

        foreach (var source in transform.SourceCoordinateSystems)
        {
            PayloadValidationRules.SourceCoordinateSystem(source, nameof(transform));
        }

        if (imageryPixelsPerSample is { } pixelsPerSample
            && (pixelsPerSample < 1 || pixelsPerSample > MaxImageryPixelsPerSample))
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageryPixelsPerSample),
                imageryPixelsPerSample,
                $"Imagery pixels per sample must be between 1 and {MaxImageryPixelsPerSample}.");
        }

        ValidateSlopeThreshold(slopeThresholds.ImpassableDegrees, "ImpassableDegrees");
        ValidateSlopeThreshold(slopeThresholds.UnbuildableDegrees, "UnbuildableDegrees");
        PayloadValidationRules.RingPositions(playableArea.OuterRing.Positions, "playableArea", nameof(playableArea));

        for (var index = 0; index < playableArea.InnerRings.Count; index++)
        {
            PayloadValidationRules.RingPositions(playableArea.InnerRings[index].Positions, $"playableArea hole {index}", nameof(playableArea));
        }

        ChunksX = chunksX;
        ChunksY = chunksY;
        Transform = transform;
        PlayableArea = playableArea;
        ImageryPixelsPerSample = imageryPixelsPerSample;
        SlopeThresholds = slopeThresholds;
    }

    public int ChunksX { get; }

    public int ChunksY { get; }

    /// <summary>Retained map-level coordinate and elevation metadata.</summary>
    public GeospatialMapTransform Transform { get; }

    /// <summary>Playable-area polygon in projected target-CRS meters, holes retained.</summary>
    public GeoPolygon PlayableArea { get; }

    /// <summary>Imagery pixels per lattice sample; null when the payload has no imagery layer.</summary>
    public int? ImageryPixelsPerSample { get; }

    public SlopeThresholds SlopeThresholds { get; }

    /// <summary>Total chunk count the descriptor list must match.</summary>
    public long TotalChunkCount => (long)ChunksX * ChunksY;

    private static void ValidateSlopeThreshold(double degrees, string propertyName)
    {
        if (!double.IsFinite(degrees) || degrees < 0d || degrees > 90d)
        {
            throw new ArgumentException(
                $"SlopeThresholds.{propertyName} must be finite and within [0, 90] degrees; found {degrees}.",
                "slopeThresholds");
        }
    }
}
