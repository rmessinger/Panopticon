using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// Pixel format codes of the AoE3 DE DDT texture container.
/// </summary>
public enum DdtFormat : byte
{
    Bgra = 1,
    Bc1 = 4,
    Bc1Alpha = 5,
    R8 = 7,
    Bc2 = 8,
    Bc3 = 9,
}

/// <summary>
/// One DDT mip level: an absolute byte offset within the decompressed DDT and
/// the recorded data size.
/// </summary>
public readonly record struct DdtMip(uint Offset, uint Size);

/// <summary>
/// Read access to an AoE3 DE DDT texture ('RTS3' header). Mip offsets are
/// absolute within the decompressed DDT; stored mips are exposed but only the
/// base mip is consumed by Panopticon.
/// </summary>
public sealed class DdtFile
{
    private readonly byte[] _data;
    private readonly DdtMip[] _mips;

    private DdtFile(byte[] data, byte usage, byte alphaBits, DdtFormat format, DdtMip[] mips, uint width, uint height)
    {
        _data = data;
        _mips = mips;
        Usage = usage;
        AlphaBits = alphaBits;
        Format = format;
        Width = width;
        Height = height;
    }

    public byte Usage { get; }

    public byte AlphaBits { get; }

    public DdtFormat Format { get; }

    public int MipCount => _mips.Length;

    public uint Width { get; }

    public uint Height { get; }

    public IReadOnlyList<DdtMip> Mips => _mips;

    public ReadOnlySpan<byte> GetMipData(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, _mips.Length);

        var mip = _mips[level];
        return _data.AsSpan((int)mip.Offset, (int)mip.Size);
    }

    public static DdtFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var data = new byte[stream.Length - stream.Position];
        stream.ReadExactly(data);

        return Read(data);
    }

    public static DdtFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 16 + 8)
        {
            throw new InvalidDataException($"DDT data is {data.Length} bytes, smaller than its header and one mip entry.");
        }

        if (data[0] != 0x52 || data[1] != 0x54 || data[2] != 0x53 || data[3] != 0x33)
        {
            throw new InvalidDataException($"Expected DDT magic 'RTS3', found '{System.Text.Encoding.ASCII.GetString(data, 0, 4)}'.");
        }

        var usage = data[4];
        var alphaBits = data[5];
        var formatCode = data[6];
        var mipCount = data[7];
        if (!Enum.IsDefined(typeof(DdtFormat), formatCode))
        {
            throw new InvalidDataException($"Unsupported DDT format code {formatCode}.");
        }

        if (mipCount == 0)
        {
            throw new InvalidDataException("DDT texture has no mip levels.");
        }

        var width = BitConverter.ToUInt32(data, 8);
        var height = BitConverter.ToUInt32(data, 12);

        var mipTableEnd = 16 + 8 * mipCount;
        if (mipTableEnd > data.Length)
        {
            throw new InvalidDataException($"DDT mip table of {mipCount} mips exceeds the {data.Length}-byte file.");
        }

        var mips = new DdtMip[mipCount];
        for (var i = 0; i < mipCount; i++)
        {
            var offset = BitConverter.ToUInt32(data, 16 + 8 * i);
            var size = BitConverter.ToUInt32(data, 16 + 8 * i + 4);
            if (offset > data.Length || size > data.Length - offset)
            {
                throw new InvalidDataException(
                    $"DDT mip {i} range {offset}..{offset + size} exceeds the {data.Length}-byte file.");
            }

            mips[i] = new DdtMip(offset, size);
        }

        return new DdtFile(data, usage, alphaBits, (DdtFormat)formatCode, mips, width, height);
    }
}
