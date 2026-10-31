// GR2 container structure referenced from LSLib (github.com/Norbyte/lslib, MIT);
// this implementation was written against game data, no code copied.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Read access to little-endian Granny GR2 version 7 files, in both
/// 64-bit and 32-bit pointer layouts. The file's embedded type tree drives parsing
/// into generic <see cref="GrannyObject"/> field maps. Supports uncompressed,
/// Oodle1, and BitKnit2 sections.
/// </summary>
public sealed class GrannyFile
{
    private static readonly byte[] CanonicalSignature =
        [0xE5, 0x9B, 0x49, 0x5E, 0x6F, 0x63, 0x1F, 0x14, 0x1E, 0x13, 0xEB, 0xA9, 0x90, 0xBE, 0xED, 0xC4];

    private static readonly byte[] AlternateSignature =
        [0xE5, 0x2F, 0x4A, 0xE1, 0x6F, 0xC2, 0x8A, 0xEE, 0x1E, 0xD2, 0xB4, 0x4C, 0x90, 0xD7, 0x55, 0xAF];

    private static readonly byte[] Signature32A =
        [0x29, 0xDE, 0x6C, 0xC0, 0xBA, 0xA4, 0x53, 0x2B, 0x25, 0xF5, 0xB7, 0xA5, 0xF6, 0x66, 0xE2, 0xEE];

    private static readonly byte[] Signature32B =
        [0x29, 0x75, 0x31, 0x82, 0xBA, 0x02, 0x11, 0x77, 0x25, 0x3A, 0x60, 0x2F, 0xF6, 0x6A, 0x8C, 0x2E];

    private const int SectionTableEntrySize = 44;
    private const int TransformSize = 68;

    private readonly byte[] _buffer;
    private readonly List<GrannySection> _sections;
    private readonly Dictionary<ulong, GrannyStructDefinition> _typeCache = new();
    private readonly Dictionary<ulong, GrannyObject> _objectCache = new();
    private readonly HashSet<ulong> _typesInProgress = new();

    private GrannyFile(byte[] buffer, List<GrannySection> sections, int pointerSize, string? filePath)
    {
        _buffer = buffer;
        _sections = sections;
        PointerSize = pointerSize;
        FilePath = filePath;
    }

    /// <summary>Pointer field width in bytes: 8 for 64-bit files, 4 for 32-bit files.</summary>
    public int PointerSize { get; }

    private int MemberSize => PointerSize == 4 ? 32 : 44;

    public string? FilePath { get; }

    public IReadOnlyList<GrannySection> Sections => _sections;

    public int StructDefinitionCount => _typeCache.Count;

    /// <summary>Root object of the file, for example ArtToolInfo, Skeletons, Meshes, Models.</summary>
    public GrannyObject Root { get; private set; } = null!;

    public static GrannyFile Read(Stream stream, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new NotSupportedException("GR2 reading requires a seekable stream.");
        }

        var name = filePath is null ? "GR2 stream" : $"GR2 file '{filePath}'";
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var pointerSize = ValidateSignature(reader, name);
        var header = GrannyHeader.Read(reader);
        ValidateHeader(header, reader.BaseStream.Length, name);

        var sections = ReadSectionTable(reader, header, filePath, name);
        var buffer = BuildBuffer(reader, sections, name);
        ApplyRelocations(reader, sections, buffer, name);
        ValidateMixedMarshalling(reader, sections, name);

        var file = new GrannyFile(buffer, sections, pointerSize, filePath);
        var rootTypeOffset = (long)sections[(int)header.RootTypeSection].OffsetInBuffer + header.RootTypeOffset;
        var rootType = file.ReadStructDefinition(rootTypeOffset);
        var rootNodeOffset = (long)sections[(int)header.RootNodeSection].OffsetInBuffer + header.RootNodeOffset;
        file.Root = file.ReadInlineObject(rootNodeOffset, rootType, out _);
        return file;
    }

    private static int ValidateSignature(BinaryReader reader, string name)
    {
        if (reader.BaseStream.Length < GrannyHeader.HeaderStart + GrannyHeader.HeaderSize)
        {
            throw new InvalidDataException($"{name} is smaller than its fixed GR2 header.");
        }

        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        var signature = reader.ReadBytes(GrannyHeader.SignatureSize);
        if (signature.AsSpan().SequenceEqual(CanonicalSignature)
            || signature.AsSpan().SequenceEqual(AlternateSignature))
        {
            return 8;
        }

        if (signature.AsSpan().SequenceEqual(Signature32A) || signature.AsSpan().SequenceEqual(Signature32B))
        {
            return 4;
        }

        throw new InvalidDataException(
            $"{name} has an unsupported GR2 signature {Convert.ToHexString(signature)}; "
            + "only little-endian 64-bit and 32-bit Granny files are supported.");
    }

    private static void ValidateHeader(GrannyHeader header, long fileLength, string name)
    {
        if (header.HeaderFormat != GrannyHeader.SupportedHeaderFormat)
        {
            throw new InvalidDataException(
                $"{name} uses header format {header.HeaderFormat}; expected {GrannyHeader.SupportedHeaderFormat}.");
        }

        if (header.Version != GrannyHeader.SupportedVersion)
        {
            throw new InvalidDataException(
                $"{name} is GR2 version {header.Version}; expected {GrannyHeader.SupportedVersion}.");
        }

        if (header.SectionCount == 0)
        {
            throw new InvalidDataException($"{name} declares no sections.");
        }

        var tableEnd = header.SectionTableOffset + (long)header.SectionCount * SectionTableEntrySize;
        if (tableEnd > fileLength)
        {
            throw new InvalidDataException($"{name} section table extends past the end of the file.");
        }

        if (header.HeadersSize != 0 && tableEnd != header.HeadersSize)
        {
            throw new InvalidDataException(
                $"{name} header size {header.HeadersSize} does not match the section table end {tableEnd}.");
        }
    }

    private static List<GrannySection> ReadSectionTable(
        BinaryReader reader, GrannyHeader header, string? filePath, string name)
    {
        var sections = new List<GrannySection>((int)header.SectionCount);
        var tableEnd = header.SectionTableOffset + (long)header.SectionCount * SectionTableEntrySize;
        reader.BaseStream.Seek(header.SectionTableOffset, SeekOrigin.Begin);

        var dataOffset = 0L;
        for (var i = 0; i < header.SectionCount; i++)
        {
            var compression = reader.ReadUInt32();
            if (compression is not (0 or 2 or 4))
            {
                throw new GrannyCompressedSectionException(filePath, i, compression);
            }

            var offsetInFile = (long)reader.ReadUInt32();
            var compressedSize = reader.ReadUInt32();
            var uncompressedSize = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // alignment
            var first16Bit = reader.ReadUInt32();
            var first8Bit = reader.ReadUInt32();
            var relocationsOffset = (long)reader.ReadUInt32();
            var relocationCount = reader.ReadUInt32();
            var marshallingOffset = (long)reader.ReadUInt32();
            var marshallingCount = reader.ReadUInt32();

            if (compressedSize > int.MaxValue || dataOffset + uncompressedSize > int.MaxValue)
            {
                throw new InvalidDataException($"{name} section {i} exceeds the 2 GiB reader limit.");
            }

            if (compression == 0 && compressedSize != uncompressedSize)
            {
                throw new InvalidDataException(
                    $"{name} section {i} claims {compressedSize} stored bytes for {uncompressedSize} raw bytes.");
            }

            if (compression == 2 && (first16Bit > first8Bit || first8Bit > uncompressedSize))
            {
                throw new InvalidDataException($"{name} section {i} has invalid Oodle1 dictionary boundaries.");
            }

            if (uncompressedSize > 0
                && (compressedSize == 0 || offsetInFile < tableEnd || offsetInFile + compressedSize > reader.BaseStream.Length))
            {
                throw new InvalidDataException($"{name} section {i} data range lies outside the file.");
            }

            if (relocationCount > int.MaxValue / 12
                || (relocationCount > 0 && (relocationsOffset < tableEnd
                    || relocationsOffset + (compression == 4 ? 4 : (long)relocationCount * 12) > reader.BaseStream.Length)))
            {
                throw new InvalidDataException($"{name} section {i} relocation table extends past the end.");
            }

            if (marshallingCount > int.MaxValue / 16
                || (marshallingCount > 0 && (marshallingOffset < tableEnd
                    || marshallingOffset + (compression == 4 ? 4 : (long)marshallingCount * 16) > reader.BaseStream.Length)))
            {
                throw new InvalidDataException($"{name} section {i} marshalling table extends past the end.");
            }

            sections.Add(new GrannySection
            {
                Compression = compression,
                OffsetInFile = offsetInFile,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                First16Bit = first16Bit,
                First8Bit = first8Bit,
                OffsetInBuffer = (int)dataOffset,
                RelocationsOffset = relocationsOffset,
                RelocationCount = relocationCount,
                MarshallingOffset = marshallingOffset,
                MarshallingCount = marshallingCount,
            });
            dataOffset += uncompressedSize;
        }

        ValidateRootReference(header, sections, name);
        return sections;
    }

    private static void ValidateRootReference(GrannyHeader header, List<GrannySection> sections, string name)
    {
        if (header.RootTypeSection >= sections.Count || header.RootNodeSection >= sections.Count)
        {
            throw new InvalidDataException(
                $"{name} root references section indices "
                + $"{header.RootTypeSection}/{header.RootNodeSection} outside the {sections.Count} sections.");
        }

        if (header.RootTypeOffset > sections[(int)header.RootTypeSection].UncompressedSize
            || header.RootNodeOffset > sections[(int)header.RootNodeSection].UncompressedSize)
        {
            throw new InvalidDataException($"{name} root type or root node offset lies outside its section.");
        }
    }

    private static byte[] BuildBuffer(BinaryReader reader, List<GrannySection> sections, string name)
    {
        long total = 0;
        foreach (var section in sections)
        {
            total += section.UncompressedSize;
        }

        if (total > int.MaxValue)
        {
            throw new InvalidDataException($"{name} decompresses past the 2 GiB reader limit.");
        }

        var buffer = new byte[total];
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.IsEmpty)
            {
                continue;
            }

            reader.BaseStream.Seek(section.OffsetInFile, SeekOrigin.Begin);
            var stored = reader.ReadBytes((int)section.CompressedSize);
            if (stored.Length != section.CompressedSize)
            {
                throw new InvalidDataException($"{name} section data ended unexpectedly.");
            }

            try
            {
                var decoded = section.Compression switch
                {
                    0 => stored,
                    2 => Oodle1Decoder.Decode(stored, (int)section.UncompressedSize, section.First16Bit, section.First8Bit),
                    4 => BitKnit2Decoder.Decode(stored, (int)section.UncompressedSize),
                    _ => throw new InvalidDataException("Unsupported compression."),
                };
                if (decoded.Length != section.UncompressedSize)
                {
                    throw new InvalidDataException("Decoded size differs from the section size.");
                }

                decoded.CopyTo(buffer, section.OffsetInBuffer);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"{name} section {i} compression {section.Compression}: {exception.Message}", exception);
            }
        }

        return buffer;
    }

    private static void ApplyRelocations(BinaryReader reader, List<GrannySection> sections, byte[] buffer, string name)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.RelocationCount == 0)
            {
                continue;
            }

            using var table = ReadFixupTable(reader, section, section.RelocationsOffset,
                checked((int)section.RelocationCount * 12), name, i, "relocation");
            for (var r = 0; r < section.RelocationCount; r++)
            {
                var position = table.ReadUInt32();
                var targetSection = table.ReadUInt32();
                var targetOffset = table.ReadUInt32();

                if ((ulong)position + 4 > section.UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"{name} section {i} relocation {r} writes at {position}, outside the section.");
                }

                if (targetSection >= sections.Count
                    || targetOffset > sections[(int)targetSection].UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"{name} section {i} relocation {r} targets {targetSection}+{targetOffset}, "
                        + "outside the file.");
                }

                // Relocations write the buffer-relative target address. 64-bit files
                // store only the low 4 bytes of each pointer (the exporter leaves the
                // high bytes zero); 32-bit pointers are 4 bytes wide altogether.
                var target = sections[(int)targetSection];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    buffer.AsSpan(section.OffsetInBuffer + (int)position),
                    (uint)(target.OffsetInBuffer + targetOffset));
            }
        }
    }

    private static void ValidateMixedMarshalling(BinaryReader reader, List<GrannySection> sections, string name)
    {
        // Mixed marshalling fixups exist for cross-endianness loading. Accepted files
        // are little-endian on a little-endian host, so entries are parsed and bounds
        // validated but no byte swaps are applied.
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.MarshallingCount == 0)
            {
                continue;
            }

            using var table = ReadFixupTable(reader, section, section.MarshallingOffset,
                checked((int)section.MarshallingCount * 16), name, i, "marshalling");
            for (var m = 0; m < section.MarshallingCount; m++)
            {
                _ = table.ReadUInt32(); // element count
                var position = table.ReadUInt32();
                var typeSection = table.ReadUInt32();
                var typeOffset = table.ReadUInt32();

                if (position > section.UncompressedSize
                    || typeSection >= sections.Count
                    || typeOffset > sections[(int)typeSection].UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"{name} section {i} marshalling entry {m} points outside the file.");
                }
            }
        }
    }

    private static BinaryReader ReadFixupTable(
        BinaryReader reader, GrannySection section, long offset, int outputSize,
        string name, int sectionIndex, string kind)
    {
        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
        var storedSize = section.Compression == 4 ? reader.ReadUInt32() : (uint)outputSize;
        if (storedSize > int.MaxValue || storedSize > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException($"{name} section {sectionIndex} {kind} stream extends past the end.");
        }

        var stored = reader.ReadBytes((int)storedSize);
        if (stored.Length != storedSize)
        {
            throw new InvalidDataException($"{name} section {sectionIndex} {kind} stream ended unexpectedly.");
        }

        try
        {
            var decoded = section.Compression == 4 ? BitKnit2Decoder.Decode(stored, outputSize) : stored;
            if (decoded.Length != outputSize)
            {
                throw new InvalidDataException($"Decoded {decoded.Length} bytes; expected {outputSize}.");
            }

            return new BinaryReader(new MemoryStream(decoded, writable: false));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{name} section {sectionIndex} {kind}: {exception.Message}", exception);
        }
    }

    private GrannyStructDefinition ReadStructDefinition(long offset)
    {
        var key = (ulong)offset;
        if (_typeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!_typesInProgress.Add(key))
        {
            throw new InvalidDataException($"GR2 type definition at buffer offset {offset} is recursive.");
        }

        try
        {
            var members = new List<GrannyMemberDefinition>();
            var cursor = offset;
            var nameRefOffset = 4;
            var typeRefOffset = 4 + PointerSize;
            var arraySizeOffset = typeRefOffset + PointerSize;
            while (true)
            {
                ValidateRange(cursor, MemberSize, "type member");
                var type = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan((int)cursor));
                if (type == 0)
                {
                    break;
                }

                if (!Enum.IsDefined(typeof(GrannyMemberType), type))
                {
                    throw new InvalidDataException(
                        $"GR2 type definition at buffer offset {offset} has unknown member type {type}.");
                }

                var nameRef = ReadPointerAt(cursor + nameRefOffset);
                members.Add(new GrannyMemberDefinition
                {
                    Type = (GrannyMemberType)type,
                    Name = nameRef == 0 ? null : ReadString((long)nameRef),
                    ReferencedTypeOffset = ReadPointerAt(cursor + typeRefOffset),
                    ArraySize = BinaryPrimitives.ReadUInt32LittleEndian(
                        _buffer.AsSpan((int)cursor + arraySizeOffset)),
                    Extra0 = BinaryPrimitives.ReadUInt32LittleEndian(
                        _buffer.AsSpan((int)cursor + arraySizeOffset + 4)),
                    Extra1 = BinaryPrimitives.ReadUInt32LittleEndian(
                        _buffer.AsSpan((int)cursor + arraySizeOffset + 8)),
                    Extra2 = BinaryPrimitives.ReadUInt32LittleEndian(
                        _buffer.AsSpan((int)cursor + arraySizeOffset + 12)),
                });
                cursor += MemberSize;
            }

            var definition = new GrannyStructDefinition { Members = members };
            _typeCache[key] = definition;
            return definition;
        }
        finally
        {
            _ = _typesInProgress.Remove(key);
        }
    }

    private object? ReadElement(ref long cursor, GrannyMemberDefinition member)
    {
        var count = member.ArraySize == 0 ? 1u : member.ArraySize;
        return member.Type switch
        {
            GrannyMemberType.EmptyReference => null,
            GrannyMemberType.String => ReadStringArray(ref cursor, member, count),
            GrannyMemberType.Transform => ReadTransformArray(ref cursor, member, count),
            GrannyMemberType.Inline => ReadInlineArray(ref cursor, member, count),
            GrannyMemberType.Reference => ReadReferenceArray(ref cursor, member, count),
            GrannyMemberType.VariantReference => ReadVariantReferenceArray(ref cursor, member, count),
            GrannyMemberType.ArrayOfReferences => ReadArrayOfReferences(ref cursor, member),
            GrannyMemberType.ReferenceToArray => ReadReferenceToArray(ref cursor, member),
            GrannyMemberType.ReferenceToVariantArray => ReadReferenceToVariantArray(ref cursor, member),
            _ => ReadScalarArray(ref cursor, member, count),
        };
    }

    private object? ReadStringArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        if (count == 1)
        {
            var reference = ReadPointer(ref cursor, Describe(member));
            return reference == 0 ? null : ReadString((long)reference);
        }

        var values = new string?[count];
        for (var i = 0; i < count; i++)
        {
            var reference = ReadPointer(ref cursor, Describe(member));
            values[i] = reference == 0 ? null : ReadString((long)reference);
        }

        return values;
    }

    private object ReadTransformArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        var values = new GrannyTransform[count];
        for (var i = 0; i < count; i++)
        {
            ValidateRange(cursor, TransformSize, Describe(member));
            values[i] = new GrannyTransform(
                BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan((int)cursor)),
                ReadFloats(cursor + 4, 3),
                ReadFloats(cursor + 16, 4),
                ReadFloats(cursor + 32, 9));
            cursor += TransformSize;
        }

        return count == 1 ? values[0] : values;
    }

    private object ReadInlineArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        var definition = ResolveReferencedType(member);
        if (count == 1)
        {
            return ReadInlineObject(cursor, definition, out cursor);
        }

        var values = new GrannyObject[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadInlineObject(cursor, definition, out cursor);
        }

        return values;
    }

    private object? ReadReferenceArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        var definition = ResolveReferencedType(member);
        if (count == 1)
        {
            var reference = ReadPointer(ref cursor, Describe(member));
            return reference == 0 ? null : ReadObjectAt(reference, definition);
        }

        var values = new GrannyObject?[count];
        for (var i = 0; i < count; i++)
        {
            var reference = ReadPointer(ref cursor, Describe(member));
            values[i] = reference == 0 ? null : ReadObjectAt(reference, definition);
        }

        return values;
    }

    private object? ReadVariantReferenceArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        if (count == 1)
        {
            return ReadVariantReference(ref cursor, member);
        }

        var values = new object?[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadVariantReference(ref cursor, member);
        }

        return values;
    }

    private object? ReadVariantReference(ref long cursor, GrannyMemberDefinition member)
    {
        ValidateRange(cursor, PointerSize * 2, Describe(member));
        var typeReference = ReadPointerAt(cursor);
        var objectReference = ReadPointerAt(cursor + PointerSize);
        cursor += PointerSize * 2;
        if (objectReference == 0)
        {
            return null;
        }

        // Variant types come from the data, not from the member definition.
        var definition = ReadStructDefinition((long)ValidateOffset(typeReference, Describe(member)));
        if (IsCurveDefinition(definition))
        {
            return ReadOpaqueCurve(objectReference, definition);
        }

        return ReadObjectAt(objectReference, definition);
    }

    // Curve data is a Granny user type: the exporter gives every curve variant
    // (DaK32fC32f, DaConstant32f, ...) the same DaK32fC32f-shaped definition,
    // and the CurveDataHeader Format byte selects the actual on-disk layout.
    // The generic walk cannot follow the type tree into such bodies. For the
    // corpus-dominant format 1 (DaK32fC32f) the reader exposes the knot and
    // control lists; other formats stay header-opaque.
    private static bool IsCurveDefinition(GrannyStructDefinition definition)
    {
        return definition.Members.Count > 0
            && definition.Members[0].Name is { } name
            && name.StartsWith("CurveDataHeader_", StringComparison.Ordinal);
    }

    private GrannyObject ReadOpaqueCurve(ulong offset, GrannyStructDefinition definition)
    {
        var position = (long)ValidateOffset(offset, "curve data");
        ValidateRange(position, 2, "curve data header");
        var format = (uint)_buffer[position];
        var value = new GrannyObject();
        value.Set("Format", format);
        value.Set("Degree", (uint)_buffer[position + 1]);
        value.Set("CurveDataKind", definition.Members[0].Name!["CurveDataHeader_".Length..]);

        // The shared definition lists [header inline, Padding, Knots RefArr, Controls RefArr].
        // Only the DaK32fC32f layout (format 1) is verified against the corpus; other
        // formats keep the body opaque and the constant-format controls stay unread until
        // a file that needs them shows up.
        if (definition.Members.Count >= 4 && format == 1)
        {
            var cursor = position + 4;
            value.Set("Knots", UnwrapFloatList(ReadReferenceToArray(ref cursor, definition.Members[2])));
            value.Set("Controls", UnwrapFloatList(ReadReferenceToArray(ref cursor, definition.Members[3])));
        }

        return value;
    }

    private static float[] UnwrapFloatList(GrannyObject[] wrapperItems)
    {
        var values = new float[wrapperItems.Length];
        for (var i = 0; i < wrapperItems.Length; i++)
        {
            foreach (var field in wrapperItems[i].Fields.Values)
            {
                if (field is float value)
                {
                    values[i] = value;
                }
            }
        }

        return values;
    }

    private GrannyObject[] ReadArrayOfReferences(ref long cursor, GrannyMemberDefinition member)
    {
        ValidateRange(cursor, 4 + PointerSize, Describe(member));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan((int)cursor));
        var offset = ReadPointerAt(cursor + 4);
        cursor += 4 + PointerSize;
        if (size == 0)
        {
            return [];
        }

        var definition = ResolveReferencedType(member);
        var position = (long)ValidateOffset(offset, Describe(member));
        ValidateRange(position, (long)size * PointerSize, Describe(member));
        var values = new GrannyObject[size];
        for (var i = 0; i < size; i++)
        {
            var reference = ReadPointerAt(position + (long)i * PointerSize);
            if (reference != 0)
            {
                values[i] = ReadObjectAt(ValidateOffset(reference, Describe(member)), definition);
            }
        }

        return values;
    }

    private GrannyObject[] ReadReferenceToArray(ref long cursor, GrannyMemberDefinition member)
    {
        ValidateRange(cursor, 4 + PointerSize, Describe(member));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan((int)cursor));
        var offset = ReadPointerAt(cursor + 4);
        cursor += 4 + PointerSize;
        if (size == 0)
        {
            return [];
        }

        // Array items are packed inline structs with no padding; reading each item
        // yields the start of the next, so no element stride is needed.
        var definition = ResolveReferencedType(member);
        var position = (long)ValidateOffset(offset, Describe(member));
        var values = new GrannyObject[size];
        for (var i = 0; i < size; i++)
        {
            values[i] = ReadInlineObject(position, definition, out position);
        }

        return values;
    }

    private GrannyObject[] ReadReferenceToVariantArray(ref long cursor, GrannyMemberDefinition member)
    {
        ValidateRange(cursor, PointerSize * 2 + 4, Describe(member));
        var typeReference = ReadPointerAt(cursor);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan((int)cursor + PointerSize));
        var offset = ReadPointerAt(cursor + PointerSize + 4);
        cursor += PointerSize * 2 + 4;
        if (size == 0)
        {
            return [];
        }

        var definition = ReadStructDefinition((long)ValidateOffset(typeReference, Describe(member)));
        if (IsCurveDefinition(definition))
        {
            throw new InvalidDataException(
                $"{Describe(member)} is an array of curve data, which has no generic layout.");
        }

        var position = (long)ValidateOffset(offset, Describe(member));
        var values = new GrannyObject[size];
        for (var i = 0; i < size; i++)
        {
            values[i] = ReadInlineObject(position, definition, out position);
        }

        return values;
    }

    private object ReadScalarArray(ref long cursor, GrannyMemberDefinition member, uint count)
    {
        var elementSize = GetScalarSize(member.Type);
        ValidateRange(cursor, (long)elementSize * count, Describe(member));
        var span = _buffer.AsSpan((int)cursor, (int)(elementSize * count));
        var result = member.Type switch
        {
            GrannyMemberType.Real32 => ReadScalars<float>(span, count, 4, member,
                (s, i) => BinaryPrimitives.ReadSingleLittleEndian(s[(i * 4)..])),
            GrannyMemberType.Int8 or GrannyMemberType.BinormalInt8
                => ReadScalars<sbyte>(span, count, 1, member, (s, i) => (sbyte)s[i]),
            GrannyMemberType.UInt8 or GrannyMemberType.NormalUInt8
                => ReadScalars<byte>(span, count, 1, member, (s, i) => s[i]),
            GrannyMemberType.Int16 or GrannyMemberType.BinormalInt16
                => ReadScalars<short>(span, count, 2, member,
                    (s, i) => BinaryPrimitives.ReadInt16LittleEndian(s[(i * 2)..])),
            GrannyMemberType.UInt16 or GrannyMemberType.NormalUInt16
                => ReadScalars<ushort>(span, count, 2, member,
                    (s, i) => BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..])),
            GrannyMemberType.Int32 => ReadScalars<int>(span, count, 4, member,
                (s, i) => BinaryPrimitives.ReadInt32LittleEndian(s[(i * 4)..])),
            GrannyMemberType.UInt32 => ReadScalars<uint>(span, count, 4, member,
                (s, i) => BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..])),
            GrannyMemberType.Real16 => ReadScalars<Half>(span, count, 2, member,
                (s, i) => BitConverter.UInt16BitsToHalf(
                    BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..]))),
            _ => throw new InvalidDataException(
                $"GR2 member '{member.Name}' has unsupported scalar type {member.Type}."),
        };
        cursor += (long)elementSize * count;
        return result;
    }

    private static object ReadScalars<T>(
        ReadOnlySpan<byte> span, uint count, int elementSize, GrannyMemberDefinition member,
        Func<ReadOnlySpan<byte>, int, T> read)
    {
        if (count == 1)
        {
            return read(span, 0);
        }

        var values = new T[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = read(span, i);
        }

        return values;
    }

    private static int GetScalarSize(GrannyMemberType type)
    {
        return type switch
        {
            GrannyMemberType.Int8 or GrannyMemberType.UInt8
                or GrannyMemberType.BinormalInt8 or GrannyMemberType.NormalUInt8 => 1,
            GrannyMemberType.Int16 or GrannyMemberType.UInt16
                or GrannyMemberType.BinormalInt16 or GrannyMemberType.NormalUInt16
                or GrannyMemberType.Real16 => 2,
            GrannyMemberType.Int32 or GrannyMemberType.UInt32 or GrannyMemberType.Real32 => 4,
            _ => 0,
        };
    }

    private GrannyObject ReadObjectAt(ulong offset, GrannyStructDefinition definition)
    {
        if (_objectCache.TryGetValue(offset, out var cached))
        {
            return cached;
        }

        var value = new GrannyObject();
        // Registered before the members are read so cyclic references terminate.
        _objectCache[offset] = value;
        var cursor = (long)ValidateOffset(offset, "object");
        ReadMembersInto(value, ref cursor, definition);
        return value;
    }

    private GrannyObject ReadInlineObject(long cursor, GrannyStructDefinition definition, out long end)
    {
        var value = new GrannyObject();
        ReadMembersInto(value, ref cursor, definition);
        end = cursor;
        return value;
    }

    private void ReadMembersInto(GrannyObject value, ref long cursor, GrannyStructDefinition definition)
    {
        for (var i = 0; i < definition.Members.Count; i++)
        {
            var member = definition.Members[i];
            value.Set(member.Name ?? $"Field{i}", ReadElement(ref cursor, member));
        }
    }

    private GrannyStructDefinition ResolveReferencedType(GrannyMemberDefinition member)
    {
        if (member.ReferencedTypeOffset == 0)
        {
            throw new InvalidDataException(
                $"GR2 member '{member.Name}' of type {member.Type} has no referenced type definition.");
        }

        return ReadStructDefinition((long)ValidateOffset(member.ReferencedTypeOffset, "referenced type"));
    }

    private ulong ReadPointer(ref long cursor, string description)
    {
        ValidateRange(cursor, PointerSize, description);
        var value = ReadPointerAt(cursor);
        cursor += PointerSize;
        return value == 0 ? 0 : ValidateOffset(value, description);
    }

    private ulong ReadPointerAt(long position)
    {
        var span = _buffer.AsSpan((int)position, PointerSize);
        return PointerSize == 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt64LittleEndian(span);
    }

    private string ReadString(long offset)
    {
        _ = ValidateOffset((ulong)offset, "string");
        var terminator = Array.IndexOf(_buffer, (byte)0, (int)offset);
        if (terminator < 0)
        {
            throw new InvalidDataException($"GR2 string at buffer offset {offset} has no terminator.");
        }

        return Encoding.UTF8.GetString(_buffer, (int)offset, terminator - (int)offset);
    }

    private float[] ReadFloats(long offset, int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(_buffer.AsSpan((int)offset + i * 4));
        }

        return values;
    }

    private ulong ValidateOffset(ulong offset, string description)
    {
        if (offset >= (ulong)_buffer.Length)
        {
            throw new InvalidDataException(
                $"GR2 {description} points at buffer offset {offset}, past the end of the data.");
        }

        return offset;
    }

    private void ValidateRange(long offset, long size, string description)
    {
        if (offset < 0 || size < 0 || offset + size > _buffer.Length)
        {
            throw new InvalidDataException(
                $"GR2 {description} range {offset}..{offset + size} leaves the {_buffer.Length}-byte data buffer.");
        }
    }

    private static string Describe(GrannyMemberDefinition member)
    {
        return member.Name is null ? $"member of type {member.Type}" : $"member '{member.Name}'";
    }
}
