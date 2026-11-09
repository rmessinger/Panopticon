#nullable enable

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Pure little-endian codecs for H3 map package chunk files. Heights round-trip exact
/// float32 bit patterns, negative zero, subnormals, and large finite values included;
/// the raw layers copy bytes unchanged. No method touches the filesystem, so encodings
/// are deterministic by construction.
/// </summary>
public static class MapPackageChunkCodec
{
    public const int BytesPerHeightSample = sizeof(float);

    public const int BytesPerRgbaPixel = 4;

    /// <summary>Serializes height samples as explicit little-endian float32 bytes, independent of host order, with exact bit preservation.</summary>
    public static byte[] EncodeHeights(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var encoded = new byte[samples.Length * BytesPerHeightSample];

        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * BytesPerHeightSample), BitConverter.SingleToUInt32Bits(samples[i]));
        }

        return encoded;
    }

    /// <summary>
    /// Decodes height samples from a little-endian float32 buffer without rounding,
    /// clamping, or finiteness filtering, so every stored bit pattern survives exactly.
    /// Returns false with a diagnostic when the byte length is not a sample multiple.
    /// </summary>
    public static bool TryDecodeHeights(
        ReadOnlyMemory<byte> encoded,
        [NotNullWhen(true)] out float[]? samples,
        [NotNullWhen(false)] out MapPackageDiagnostic? error)
    {
        samples = null;
        error = null;

        if (encoded.Length % BytesPerHeightSample != 0)
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.LengthMismatch,
                $"The heights buffer holds {encoded.Length} bytes; it must be a multiple of {BytesPerHeightSample}.");
            return false;
        }

        var span = encoded.Span;
        var decoded = new float[encoded.Length / BytesPerHeightSample];

        for (var i = 0; i < decoded.Length; i++)
        {
            decoded[i] = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i * BytesPerHeightSample)));
        }

        samples = decoded;
        return true;
    }

    /// <summary>Serializes one materials layer as raw uint8 palette-index bytes.</summary>
    public static byte[] EncodeMaterials(byte[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        return CopyRaw(indices);
    }

    /// <summary>Decodes one materials layer from its raw uint8 index bytes.</summary>
    public static byte[] DecodeMaterials(ReadOnlyMemory<byte> encoded)
    {
        return encoded.ToArray();
    }

    /// <summary>Serializes one masks layer as raw uint8 flag bytes.</summary>
    public static byte[] EncodeMasks(byte[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return CopyRaw(flags);
    }

    /// <summary>Decodes one masks layer from its raw uint8 flag bytes.</summary>
    public static byte[] DecodeMasks(ReadOnlyMemory<byte> encoded)
    {
        return encoded.ToArray();
    }

    /// <summary>Serializes one imagery layer as raw RGBA8 pixel bytes.</summary>
    public static byte[] EncodeImagery(byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        return CopyRaw(rgba);
    }

    /// <summary>Decodes one imagery layer from its raw RGBA8 pixel bytes.</summary>
    public static byte[] DecodeImagery(ReadOnlyMemory<byte> encoded)
    {
        return encoded.ToArray();
    }

    /// <summary>Serializes one retained section's opaque bytes unchanged.</summary>
    public static byte[] EncodeRetained(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return CopyRaw(bytes);
    }

    /// <summary>Decodes one retained section from its opaque bytes unchanged.</summary>
    public static byte[] DecodeRetained(ReadOnlyMemory<byte> encoded)
    {
        return encoded.ToArray();
    }

    private static byte[] CopyRaw(byte[] data)
    {
        var encoded = new byte[data.Length];
        Array.Copy(data, encoded, data.Length);
        return encoded;
    }
}
