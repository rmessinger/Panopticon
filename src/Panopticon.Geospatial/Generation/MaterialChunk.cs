using System;
using System.Security.Cryptography;
using Panopticon.Geospatial.Grid;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// One immutable material chunk (B4b deep dive section 5.2):
/// <see cref="ChunkLayout.PointsPerChunkAxis"/>^2 byte indices into the shared palette,
/// row-major from the southern core row, column 0 the western core column; material
/// chunks carry no apron. The constructor copies the buffer, so later mutation of the
/// caller's array cannot change the chunk.
/// </summary>
public sealed class MaterialChunk
{
    private readonly byte[] _indices;

    public MaterialChunk(int chunkX, int chunkY, MaterialPalette palette, ReadOnlyMemory<byte> indices)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        ArgumentNullException.ThrowIfNull(palette);

        var expected = ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis;
        if (indices.Length != expected)
        {
            throw new ArgumentException(
                $"The index buffer must hold {expected} bytes ({ChunkLayout.PointsPerChunkAxis} per axis); found {indices.Length}.",
                nameof(indices));
        }

        var copy = indices.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] >= palette.Count)
            {
                throw new ArgumentException(
                    $"The index {copy[i]} at position {i} does not address an entry of the {palette.Count}-entry palette.",
                    nameof(indices));
            }
        }

        ChunkX = chunkX;
        ChunkY = chunkY;
        Palette = palette;
        _indices = copy;
        Indices = new ReadOnlyMemory<byte>(_indices);

        // Indices are bytes with no multi-byte values, so the hash has no byte order.
        ContentSha256Hex = Convert.ToHexString(SHA256.HashData(_indices)).ToLowerInvariant();
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    public MaterialPalette Palette { get; }

    /// <summary>Material indices; a private copy of the caller's buffer.</summary>
    public ReadOnlyMemory<byte> Indices { get; }

    /// <summary>SHA-256 of the stored index bytes, lowercase hex.</summary>
    public string ContentSha256Hex { get; }

    /// <summary>Material index at core position (x east, y north), each 0 through 128.</summary>
    public byte GetIndex(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(x, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, ChunkLayout.PointsPerChunkAxis);
        ArgumentOutOfRangeException.ThrowIfLessThan(y, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, ChunkLayout.PointsPerChunkAxis);

        return _indices[(y * ChunkLayout.PointsPerChunkAxis) + x];
    }
}
