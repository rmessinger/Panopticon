using System;
using System.Security.Cryptography;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// One immutable imagery chunk: the fixed-encoder RGBA8 PNG payload (square
/// pixelsPerAxis x pixelsPerAxis, north row first) and its SHA-256. The constructor
/// copies the payload, so later mutation of the caller's buffer cannot change the chunk.
/// </summary>
public sealed class ImageryChunk
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly byte[] _png;

    public ImageryChunk(int chunkX, int chunkY, int pixelsPerAxis, ReadOnlyMemory<byte> png)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerAxis);

        if (png.Length < PngSignature.Length || !png.Span[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            throw new ArgumentException("The payload must start with the PNG signature.", nameof(png));
        }

        ChunkX = chunkX;
        ChunkY = chunkY;
        PixelsPerAxis = pixelsPerAxis;
        _png = png.ToArray();
        Png = new ReadOnlyMemory<byte>(_png);
        PngSha256Hex = Convert.ToHexString(SHA256.HashData(_png)).ToLowerInvariant();
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    /// <summary>Output pixels per axis: 128 * imageryPixelsPerSample.</summary>
    public int PixelsPerAxis { get; }

    /// <summary>Encoded PNG bytes (RGBA8, no palette, no interlace); a private copy of the caller's buffer.</summary>
    public ReadOnlyMemory<byte> Png { get; }

    /// <summary>SHA-256 of the encoded PNG bytes, lowercase hex.</summary>
    public string PngSha256Hex { get; }
}
