#nullable enable

using System;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Terrain;

/// <summary>
/// Mutable runtime copy of one chunk. Heights use the stored layout with apron, row-major, row 0 southern.
/// Materials and masks are core-only, row-major, <c>CoreWidth</c> values per row.
/// </summary>
public sealed class TerrainChunkData
{
    private TerrainChunkData(
        ChunkKey key,
        int coreWidth,
        int coreHeight,
        float[] storedHeights,
        byte[]? coreMaterials,
        byte[]? coreMasks)
    {
        Key = key;
        CoreWidth = coreWidth;
        CoreHeight = coreHeight;
        StoredHeights = storedHeights;
        CoreMaterials = coreMaterials;
        CoreMasks = coreMasks;
    }

    public ChunkKey Key { get; }

    public int CoreWidth { get; }

    public int CoreHeight { get; }

    public float[] StoredHeights { get; }

    public byte[]? CoreMaterials { get; }

    public byte[]? CoreMasks { get; }

    public bool IsDirty { get; internal set; }

    public static TerrainChunkData FromChunk(Chunk chunk, int coreWidth, int coreHeight)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (coreWidth is < 1 or > MapChunkGeometry.CorePointsPerAxis || coreHeight is < 1 or > MapChunkGeometry.CorePointsPerAxis)
        {
            throw new ArgumentException($"Core extents {coreWidth} x {coreHeight} must each lie within 1..{MapChunkGeometry.CorePointsPerAxis}.");
        }

        var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var storedHeight = coreHeight + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var heights = chunk.Heights.Samples;

        if (heights.Count != storedWidth * storedHeight)
        {
            throw new ArgumentException(
                $"Chunk ({chunk.Key.Cx}, {chunk.Key.Cy}) holds {heights.Count} height samples; {storedWidth} x {storedHeight} are required.");
        }

        var coreCount = coreWidth * coreHeight;

        if (chunk.Materials is { } materials && materials.Indices.Count != coreCount)
        {
            throw new ArgumentException(
                $"Chunk ({chunk.Key.Cx}, {chunk.Key.Cy}) holds {materials.Indices.Count} material indices; {coreCount} are required.");
        }

        if (chunk.Masks is { } masks && masks.Flags.Count != coreCount)
        {
            throw new ArgumentException(
                $"Chunk ({chunk.Key.Cx}, {chunk.Key.Cy}) holds {masks.Flags.Count} mask flags; {coreCount} are required.");
        }

        return new TerrainChunkData(
            chunk.Key,
            coreWidth,
            coreHeight,
            heights.ToArray(),
            chunk.Materials?.Indices.ToArray(),
            chunk.Masks?.Flags.ToArray());
    }

    public Chunk ToChunk()
    {
        return new Chunk(
            Key,
            new HeightLayer(StoredHeights),
            CoreMaterials is null ? null : new MaterialLayer(CoreMaterials),
            CoreMasks is null ? null : new MaskLayer(CoreMasks),
            null);
    }
}
