using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// The 0x20-byte header that follows the 16-byte signature of a GR2 file,
/// plus the container fields between the signature and the header.
/// </summary>
internal sealed class GrannyHeader
{
    public const uint SupportedVersion = 7;
    public const uint SupportedHeaderFormat = 0;

    public const int SignatureSize = 0x10;
    public const int HeaderStart = 0x20;
    public const int HeaderSize = 0x28;

    public uint HeadersSize { get; init; }

    public uint HeaderFormat { get; init; }

    public uint Version { get; init; }

    public uint FileSize { get; init; }

    public uint SectionsOffset { get; init; }

    public uint SectionCount { get; init; }

    public uint RootTypeSection { get; init; }

    public uint RootTypeOffset { get; init; }

    public uint RootNodeSection { get; init; }

    public uint RootNodeOffset { get; init; }

    public long SectionTableOffset => HeaderStart + SectionsOffset;

    public static GrannyHeader Read(BinaryReader reader)
    {
        reader.BaseStream.Seek(0x10, SeekOrigin.Begin);
        var headersSize = reader.ReadUInt32();
        var headerFormat = reader.ReadUInt32();
        _ = reader.ReadUInt64(); // reserved

        reader.BaseStream.Seek(HeaderStart, SeekOrigin.Begin);
        var version = reader.ReadUInt32();
        var fileSize = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // crc, not validated
        var sectionsOffset = reader.ReadUInt32();
        var sectionCount = reader.ReadUInt32();
        var rootTypeSection = reader.ReadUInt32();
        var rootTypeOffset = reader.ReadUInt32();
        var rootNodeSection = reader.ReadUInt32();
        var rootNodeOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // tag

        return new GrannyHeader
        {
            HeadersSize = headersSize,
            HeaderFormat = headerFormat,
            Version = version,
            FileSize = fileSize,
            SectionsOffset = sectionsOffset,
            SectionCount = sectionCount,
            RootTypeSection = rootTypeSection,
            RootTypeOffset = rootTypeOffset,
            RootNodeSection = rootNodeSection,
            RootNodeOffset = rootNodeOffset,
        };
    }
}
