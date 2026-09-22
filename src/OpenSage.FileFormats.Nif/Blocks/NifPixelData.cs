using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>One texcache mip level: dimensions and its byte offset into the block's pixel data.</summary>
public sealed record NifPixelDataMip(uint Width, uint Height, uint Offset);

/// <summary>
/// NiPixelData at 10.1.0.0 as written by the EE2 cooker into
/// cache/texcache/*.texcache containers. DXT block bytes are preserved
/// verbatim; nothing is transcoded. Wire layout validated byte-exact against
/// the three target textures and a full-corpus census (formats 4 and 5 only,
/// format repeat always equal, palette always absent).
/// </summary>
public sealed record NifPixelData : NifBlock
{
    public const uint FormatDxt1 = 4;
    public const uint FormatDxt3 = 5;
    public const uint FormatDxt5 = 6;

    public uint Format { get; }
    public IReadOnlyList<NifPixelDataMip> Mips { get; }
    public byte[] PixelData { get; }

    private NifPixelData(NifBinaryReader reader)
    {
        Format = reader.ReadUInt32("PixelFormat");
        if (Format is not (FormatDxt1 or FormatDxt3 or FormatDxt5))
        {
            throw new NotSupportedException(
                $"NIF pixel data format {Format} is not supported; only DXT1 ({FormatDxt1}), " +
                $"DXT3 ({FormatDxt3}), and DXT5 ({FormatDxt5}) decode.");
        }

        _ = reader.ReadUInt32("RedMask");
        _ = reader.ReadUInt32("GreenMask");
        _ = reader.ReadUInt32("BlueMask");
        _ = reader.ReadUInt32("AlphaMask");
        _ = reader.ReadUInt32("BitsPerPixel");

        var formatRepeat = reader.ReadUInt32("PixelFormatRepeat");
        if (formatRepeat != Format)
        {
            throw new InvalidDataException(
                $"NIF pixel data repeats format {formatRepeat}; expected {Format}.");
        }

        _ = reader.ReadUInt32("Unknown1");
        _ = reader.ReadUInt32("Unknown2");

        var palette = reader.ReadBlockReference("Palette");
        if (palette != -1)
        {
            throw new NotSupportedException(
                $"NIF pixel data references palette block {palette}; palettes are not supported.");
        }

        var numMips = reader.ReadElementCount(12, "NumMipmaps");
        _ = reader.ReadUInt32("BytesPerPixel");

        var mips = new NifPixelDataMip[numMips];
        for (var i = 0; i < numMips; i++)
        {
            mips[i] = new NifPixelDataMip(
                reader.ReadUInt32($"Mipmaps[{i}].Width"),
                reader.ReadUInt32($"Mipmaps[{i}].Height"),
                reader.ReadUInt32($"Mipmaps[{i}].Offset"));
        }

        Mips = mips;

        var numPixels = reader.ReadElementCount(1, "NumPixels");
        PixelData = new byte[numPixels];
        var bytesRead = reader.ReadBytes(PixelData);
        if (bytesRead != numPixels)
        {
            throw new InvalidDataException(
                $"NIF pixel data declares {numPixels} bytes but only {bytesRead} remain.");
        }

        ValidateMipChain();
    }

    internal static NifPixelData Read(NifBinaryReader reader)
    {
        return new NifPixelData(reader);
    }

    /// <summary>DXT bytes per 4x4 block for this format.</summary>
    public uint BytesPerBlock => Format switch
    {
        FormatDxt1 => 8,
        FormatDxt3 or FormatDxt5 => 16,
        _ => throw new NotSupportedException($"NIF pixel data format {Format}."),
    };

    public long GetMipSize(int mipIndex)
    {
        var mip = Mips[mipIndex];
        return BytesPerBlock * BlocksPerRow(mip.Width) * BlocksPerColumn(mip.Height);
    }

    private static uint BlocksPerRow(uint width) => Math.Max(1, (width + 3) / 4);

    private static uint BlocksPerColumn(uint height) => Math.Max(1, (height + 3) / 4);

    private void ValidateMipChain()
    {
        long expectedOffset = 0;
        long total = 0;
        for (var i = 0; i < Mips.Count; i++)
        {
            var mip = Mips[i];
            if (mip.Offset != expectedOffset)
            {
                throw new InvalidDataException(
                    $"NIF pixel data mip {i} has offset {mip.Offset}; expected the contiguous " +
                    $"offset {expectedOffset}.");
            }

            var size = GetMipSize(i);
            expectedOffset += size;
            total += size;
        }

        if (total != PixelData.Length)
        {
            throw new InvalidDataException(
                $"NIF pixel data mip chain totals {total} bytes but the block declares " +
                $"{PixelData.Length}.");
        }
    }
}
