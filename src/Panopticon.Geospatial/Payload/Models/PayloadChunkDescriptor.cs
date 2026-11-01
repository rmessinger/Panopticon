using System;
using Panopticon.Geospatial.Grid;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// One chunk's file inventory: terrain, material, and mask references plus an optional
/// imagery reference, on canonical paths with the fixed section 6.1 byte lengths.
/// </summary>
public sealed record PayloadChunkDescriptor
{
    public const int TerrainChunkBytes =
        ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis * sizeof(float);

    public const int MaterialChunkBytes =
        ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis;

    public const int MaskChunkBytes =
        ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis;

    public PayloadChunkDescriptor(
        int chunkX,
        int chunkY,
        PayloadFileReference terrain,
        PayloadFileReference materials,
        PayloadFileReference masks,
        PayloadFileReference? imagery = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(masks);

        ValidateReference(terrain, PayloadChunkPaths.Terrain(chunkX, chunkY), TerrainChunkBytes, nameof(terrain));
        ValidateReference(materials, PayloadChunkPaths.Materials(chunkX, chunkY), MaterialChunkBytes, nameof(materials));
        ValidateReference(masks, PayloadChunkPaths.Masks(chunkX, chunkY), MaskChunkBytes, nameof(masks));

        if (imagery is not null)
        {
            ValidateReference(imagery, PayloadChunkPaths.Imagery(chunkX, chunkY), null, nameof(imagery));
        }

        ChunkX = chunkX;
        ChunkY = chunkY;
        Terrain = terrain;
        Materials = materials;
        Masks = masks;
        Imagery = imagery;
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    public PayloadFileReference Terrain { get; }

    public PayloadFileReference Materials { get; }

    public PayloadFileReference Masks { get; }

    /// <summary>RGBA8 PNG reference; null when the payload carries no imagery layer.</summary>
    public PayloadFileReference? Imagery { get; }

    private static void ValidateReference(PayloadFileReference reference, string expectedPath, int? expectedLength, string paramName)
    {
        if (!string.Equals(reference.Path, expectedPath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {paramName} reference path '{reference.Path}' is not the canonical path '{expectedPath}'.",
                paramName);
        }

        if (expectedLength is { } length && reference.LengthBytes != length)
        {
            throw new ArgumentException(
                $"The {paramName} reference must be exactly {length} bytes; found {reference.LengthBytes}.",
                paramName);
        }
    }
}
