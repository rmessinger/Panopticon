using System;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// One section table entry (44 bytes on disk) plus the offset of the
/// section's bytes within the concatenated uncompressed buffer.
/// </summary>
public sealed class GrannySection
{
    public uint Compression { get; init; }

    public long OffsetInFile { get; init; }

    public uint CompressedSize { get; init; }

    public uint UncompressedSize { get; init; }

    public uint First16Bit { get; init; }

    public uint First8Bit { get; init; }

    public int OffsetInBuffer { get; init; }

    internal long RelocationsOffset { get; init; }

    internal uint RelocationCount { get; init; }

    internal long MarshallingOffset { get; init; }

    internal uint MarshallingCount { get; init; }

    public bool IsEmpty => UncompressedSize == 0;
}
