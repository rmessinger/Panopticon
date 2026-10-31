namespace Panopticon.Geospatial.Grid;

/// <summary>
/// Fixed terrain lattice and chunk geometry (B4b deep dive section 5.1a). One chunk spans
/// <see cref="SampleSpacingGameUnits"/> * <see cref="IntervalsPerChunk"/> game units per axis.
/// </summary>
public static class ChunkLayout
{
    /// <summary>Lattice spacing between adjacent samples, in game units.</summary>
    public const int SampleSpacingGameUnits = 10;

    /// <summary>Sample intervals per chunk axis.</summary>
    public const int IntervalsPerChunk = 128;

    /// <summary>Lattice points per chunk axis; edge points are duplicated in both neighbours.</summary>
    public const int PointsPerChunkAxis = 129;

    /// <summary>Read-only apron samples added on each side for normals.</summary>
    public const int ApronSamplesPerSide = 1;

    /// <summary>Stored lattice points per chunk axis, including the apron on both sides.</summary>
    public const int StoredPointsPerChunkAxis = 131;

    /// <summary>Chunk span in game units: <see cref="SampleSpacingGameUnits"/> * <see cref="IntervalsPerChunk"/>.</summary>
    public const int ChunkSpanGameUnits = SampleSpacingGameUnits * IntervalsPerChunk;
}
