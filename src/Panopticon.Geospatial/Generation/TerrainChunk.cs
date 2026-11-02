using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Panopticon.Geospatial.Grid;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// One immutable terrain chunk (B4b deep dive section 5.1a): stored heights are
/// <see cref="ChunkLayout.StoredPointsPerChunkAxis"/>^2 float32 game units, row-major from
/// the southern apron row, column 0 the western apron. The constructor copies the buffer;
/// min, max, and the hash cover all stored samples, aprons included.
/// </summary>
public sealed class TerrainChunk
{
    private readonly float[] _heights;

    public TerrainChunk(int chunkX, int chunkY, ReadOnlyMemory<float> heights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);

        var expected = ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis;
        if (heights.Length != expected)
        {
            throw new ArgumentException(
                $"The height buffer must hold {expected} samples ({ChunkLayout.StoredPointsPerChunkAxis} per stored axis); found {heights.Length}.",
                nameof(heights));
        }

        _heights = heights.ToArray();
        Heights = new ReadOnlyMemory<float>(_heights);

        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        foreach (var height in _heights)
        {
            if (!float.IsFinite(height))
            {
                throw new ArgumentException("Heights must be finite float32 values.", nameof(heights));
            }

            if (height < min)
            {
                min = height;
            }

            if (height > max)
            {
                max = height;
            }
        }

        ChunkX = chunkX;
        ChunkY = chunkY;
        MinHeightGameUnits = min;
        MaxHeightGameUnits = max;
        ContentSha256Hex = ComputeHash(_heights);
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    /// <summary>Stored heights in game units; a private copy of the caller's buffer.</summary>
    public ReadOnlyMemory<float> Heights { get; }

    /// <summary>Lowest stored height, apron samples included.</summary>
    public float MinHeightGameUnits { get; }

    /// <summary>Highest stored height, apron samples included.</summary>
    public float MaxHeightGameUnits { get; }

    /// <summary>SHA-256 of the stored heights as explicit little-endian float32 bytes, lowercase hex.</summary>
    public string ContentSha256Hex { get; }

    /// <summary>Height at stored position (storedX east, storedY north), each 0 through 130.</summary>
    public float GetHeight(int storedX, int storedY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(storedX, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(storedX, ChunkLayout.StoredPointsPerChunkAxis);
        ArgumentOutOfRangeException.ThrowIfLessThan(storedY, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(storedY, ChunkLayout.StoredPointsPerChunkAxis);

        return _heights[(storedY * ChunkLayout.StoredPointsPerChunkAxis) + storedX];
    }

    private static string ComputeHash(float[] heights)
    {
        var bytes = new byte[heights.Length * sizeof(float)];

        for (var i = 0; i < heights.Length; i++)
        {
            // Explicit little-endian float32 serialization, independent of host order.
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), heights[i]);
        }

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
