using System;
using System.Security.Cryptography;
using Panopticon.Geospatial.Grid;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// One immutable terrain mask chunk (B4b deep dive section 5.1):
/// <see cref="ChunkLayout.PointsPerChunkAxis"/>^2 flag bytes, row-major from the
/// southern core row, column 0 the western core column; mask chunks carry no apron. The
/// constructor copies the buffer, so later mutation of the caller's array cannot change
/// the chunk.
/// </summary>
public sealed class TerrainMaskChunk
{
    private static readonly byte DefinedBits = (byte)(TerrainMaskFlags.Impassable | TerrainMaskFlags.Unbuildable);

    private readonly byte[] _flags;

    public TerrainMaskChunk(int chunkX, int chunkY, ReadOnlyMemory<byte> flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);

        var expected = ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis;
        if (flags.Length != expected)
        {
            throw new ArgumentException(
                $"The flag buffer must hold {expected} bytes ({ChunkLayout.PointsPerChunkAxis} per axis); found {flags.Length}.",
                nameof(flags));
        }

        var copy = flags.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if ((copy[i] & ~DefinedBits) != 0)
            {
                throw new ArgumentException(
                    $"The flag byte {copy[i]} at position {i} is not a defined {nameof(TerrainMaskFlags)} combination.",
                    nameof(flags));
            }
        }

        ChunkX = chunkX;
        ChunkY = chunkY;
        _flags = copy;
        Flags = new ReadOnlyMemory<byte>(_flags);

        // Flag bytes carry no multi-byte values, so the hash has no byte order.
        ContentSha256Hex = Convert.ToHexString(SHA256.HashData(_flags)).ToLowerInvariant();
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    /// <summary>Flag bytes, one <see cref="TerrainMaskFlags"/> per core sample; a private copy of the caller's buffer.</summary>
    public ReadOnlyMemory<byte> Flags { get; }

    /// <summary>SHA-256 of the stored flag bytes, lowercase hex.</summary>
    public string ContentSha256Hex { get; }

    /// <summary>Flags at core position (x east, y north), each 0 through 128.</summary>
    public TerrainMaskFlags GetFlags(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(x, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, ChunkLayout.PointsPerChunkAxis);
        ArgumentOutOfRangeException.ThrowIfLessThan(y, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, ChunkLayout.PointsPerChunkAxis);

        return (TerrainMaskFlags)_flags[(y * ChunkLayout.PointsPerChunkAxis) + x];
    }
}
