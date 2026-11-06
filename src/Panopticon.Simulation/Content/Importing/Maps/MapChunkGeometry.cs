#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Chunk geometry; values equal the Geospatial ChunkLayout and TerrainMaskFlags, copied because Simulation does not reference Geospatial.</summary>
public static class MapChunkGeometry
{
    public const int CorePointsPerAxis = 129;

    public const int ApronSamplesPerSide = 1;

    public const int StoredPointsPerAxis = CorePointsPerAxis + 2 * ApronSamplesPerSide;

    public const int CoreCellsPerChunk = CorePointsPerAxis * CorePointsPerAxis;

    public const int StoredHeightSamplesPerChunk = StoredPointsPerAxis * StoredPointsPerAxis;
}
