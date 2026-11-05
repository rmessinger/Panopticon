namespace Panopticon.Geospatial.Preview;

/// <summary>
/// Half-open chunk rectangle <c>[lo, hi)</c> of a payload crop (B4b group 5 decision 4).
/// The whole map is <c>(0, 0, chunksX, chunksY)</c>.
/// </summary>
public readonly record struct PreviewCrop(int ChunkXLo, int ChunkYLo, int ChunkXHi, int ChunkYHi)
{
    public static PreviewCrop WholeMap(int chunksX, int chunksY) => new(0, 0, chunksX, chunksY);
}
