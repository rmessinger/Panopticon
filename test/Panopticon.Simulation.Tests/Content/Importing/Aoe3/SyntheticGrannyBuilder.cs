using System;
using System.Collections.Generic;
using System.Text;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Builds a minimal synthetic GR2 version 7 little-endian file (64-bit or 32-bit
/// pointer layout) with one section: strings, four struct definitions, and an
/// object graph that covers every reference kind (reference, reference-to-array,
/// array-of-references, variant reference) plus scalars, a fixed array, and a
/// transform.
/// </summary>
internal static class SyntheticGrannyBuilder
{
    private static readonly byte[] Signature =
        [0xE5, 0x9B, 0x49, 0x5E, 0x6F, 0x63, 0x1F, 0x14, 0x1E, 0x13, 0xEB, 0xA9, 0x90, 0xBE, 0xED, 0xC4];

    private static readonly byte[] Signature32 =
        [0x29, 0xDE, 0x6C, 0xC0, 0xBA, 0xA4, 0x53, 0x2B, 0x25, 0xF5, 0xB7, 0xA5, 0xF6, 0x66, 0xE2, 0xEE];

    public static byte[] BuildValid()
    {
        return Build(8);
    }

    public static byte[] BuildValid32Bit()
    {
        return Build(4);
    }

    public static byte[] BuildWithBadSignature()
    {
        var file = Build(8);
        for (var i = 0; i < 16; i++)
        {
            file[i] = 0;
        }

        return file;
    }

    public static byte[] BuildWithVersion(uint version)
    {
        var file = Build(8);
        WriteUInt32(file, 0x20, version);
        return file;
    }

    public static byte[] BuildWithHeaderFormat(uint headerFormat)
    {
        var file = Build(8);
        WriteUInt32(file, 0x14, headerFormat);
        return file;
    }

    public static byte[] BuildWithCompressedSection(uint compression)
    {
        var file = Build(8);
        WriteUInt32(file, 0x68, compression);
        return file;
    }

    private static byte[] Build(int pointerSize)
    {
        var memberSize = pointerSize == 4 ? 36 : 44;
        var buffer = new List<byte>(4096);
        var relocations = new List<(uint Position, uint TargetOffset)>();

        // Offset 0 is reserved as the null pointer sentinel, so no real string
        // may start there.
        buffer.Add(0);

        void AddUInt32(uint value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        void AddInt32(int value)
        {
            AddUInt32(unchecked((uint)value));
        }

        void AddSingle(float value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        long AddString(string value)
        {
            var offset = buffer.Count;
            buffer.AddRange(Encoding.UTF8.GetBytes(value));
            buffer.Add(0);
            return offset;
        }

        void AddPointer(long targetOffset)
        {
            var position = buffer.Count;
            if (pointerSize == 4)
            {
                buffer.AddRange(BitConverter.GetBytes(0u));
            }
            else
            {
                buffer.AddRange(BitConverter.GetBytes(0UL));
            }

            relocations.Add(((uint)position, (uint)targetOffset));
        }

        long AddMemberPlaceholder()
        {
            var offset = buffer.Count;
            for (var i = 0; i < memberSize; i++)
            {
                buffer.Add(0);
            }

            return offset;
        }

        // Strings.
        var nameOffset = AddString("Name");
        var idOffset = AddString("Id");
        var coordsOffset = AddString("Coords");
        var placementOffset = AddString("Placement");
        var childOffsetName = AddString("Child");
        var childrenOffsetName = AddString("Children");
        var tagsOffsetName = AddString("Tags");
        var payloadOffsetName = AddString("Payload");
        var labelOffset = AddString("Label");
        var valuesOffset = AddString("Values");
        var textOffset = AddString("Text");
        var valueOffset = AddString("Value");
        var rootNameOffset = AddString("synthetic root");
        var firstNameOffset = AddString("first");
        var secondNameOffset = AddString("second");
        var alphaOffset = AddString("alpha");
        var betaOffset = AddString("beta");
        var curveHdrNameOffset = AddString("CurveDataHeader_Test");
        var curveNameOffset = AddString("Curve");
        var formatOffset = AddString("Format");
        var degreeOffset = AddString("Degree");
        var real32Offset = AddString("Real32");
        var paddingOffsetName2 = AddString("Padding");
        var knotsOffsetName = AddString("Knots");
        var controlsOffsetName = AddString("Controls");

        // Struct definitions, each a list of 44-byte member entries plus a
        // zero terminator. Members are placeholders filled once object offsets
        // are known.
        var tagDefinitionOffset = buffer.Count;
        var textMember = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        var childDefinitionOffset = buffer.Count;
        var labelMember = AddMemberPlaceholder();
        var valuesMember = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        var variantDefinitionOffset = buffer.Count;
        var valueMember = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        // A curve-style user type shaped like the DaK32fC32f definition.
        var real32DefinitionOffset = buffer.Count;
        var real32Member = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        var curveFormatDefinitionOffset = buffer.Count;
        var formatMemberPlaceholder = AddMemberPlaceholder();
        var degreeMemberPlaceholder = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        var curveDefinitionOffset = buffer.Count;
        var curveHeaderMember = AddMemberPlaceholder();
        var curvePaddingMember = AddMemberPlaceholder();
        var curveKnotsMember = AddMemberPlaceholder();
        var curveControlsMember = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        var rootDefinitionOffset = buffer.Count;
        var nameMember = AddMemberPlaceholder();
        var idMember = AddMemberPlaceholder();
        var coordsMember = AddMemberPlaceholder();
        var placementMember = AddMemberPlaceholder();
        var childMember = AddMemberPlaceholder();
        var childrenMember = AddMemberPlaceholder();
        var tagsMember = AddMemberPlaceholder();
        var payloadMember = AddMemberPlaceholder();
        var curveMember = AddMemberPlaceholder();
        AddTerminator(buffer, memberSize);

        // Objects.
        var tag0Offset = buffer.Count;
        AddPointer(alphaOffset);

        var tag1Offset = buffer.Count;
        AddPointer(betaOffset);

        // Curve-style user type: the data deliberately does not match the
        // DaK32fC32f-shaped definition, so a member walk would fail; only the
        // two header bytes may be read.
        var curveObjectOffset = buffer.Count;
        AddUInt32(3);
        AddUInt32(0xE6000000);
        AddUInt32(0x0018DDDE);
        AddUInt32(0x0102F250);

        // Reference-to-array items are packed inline structs.
        var childrenItemsOffset = buffer.Count;
        AddPointer(firstNameOffset);
        AddInt32(10);
        AddInt32(20);
        AddInt32(30);
        AddPointer(secondNameOffset);
        AddInt32(40);
        AddInt32(50);
        AddInt32(60);

        var variantObjectOffset = buffer.Count;
        AddInt32(77);

        var tagsReferencesOffset = buffer.Count;
        AddPointer(tag0Offset);
        AddPointer(tag1Offset);

        var rootObjectOffset = buffer.Count;
        AddPointer(rootNameOffset); // Name
        AddInt32(42); // Id
        AddSingle(1.5f);
        AddSingle(-2.5f);
        AddSingle(3.5f); // Coords[3]
        AddUInt32(7); // Placement flags: translation | rotation | scale-shear
        AddSingle(1f);
        AddSingle(2f);
        AddSingle(3f); // translation
        AddSingle(0f);
        AddSingle(0.70710678f);
        AddSingle(0f);
        AddSingle(0.70710678f); // rotation xyzw
        AddSingle(1f);
        AddSingle(0f);
        AddSingle(0f);
        AddSingle(0f);
        AddSingle(1f);
        AddSingle(0f);
        AddSingle(0f);
        AddSingle(0f);
        AddSingle(1f); // scale-shear identity
        AddPointer(childrenItemsOffset); // Child: the first packed child item
        AddUInt32(2);
        AddPointer(childrenItemsOffset); // Children: 2 packed child items
        AddUInt32(2);
        AddPointer(tagsReferencesOffset); // Tags: 2 references
        AddPointer(variantDefinitionOffset); // Payload variant type from data
        AddPointer(variantObjectOffset);
        AddPointer(curveDefinitionOffset); // Curve variant type from data
        AddPointer(curveObjectOffset);

        // Members are filled once every target offset is known.
        WriteMember(buffer, memberSize, pointerSize, textMember, 8, textOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, labelMember, 8, labelOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, valuesMember, 19, valuesOffset, 0, 3);
        WriteMember(buffer, memberSize, pointerSize, valueMember, 19, valueOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, nameMember, 8, nameOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, idMember, 19, idOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, coordsMember, 10, coordsOffset, 0, 3);
        WriteMember(buffer, memberSize, pointerSize, placementMember, 9, placementOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, childMember, 2, childOffsetName, childDefinitionOffset, 0);
        WriteMember(buffer, memberSize, pointerSize, childrenMember, 3, childrenOffsetName, childDefinitionOffset, 0);
        WriteMember(buffer, memberSize, pointerSize, tagsMember, 4, tagsOffsetName, tagDefinitionOffset, 0);
        WriteMember(buffer, memberSize, pointerSize, payloadMember, 5, payloadOffsetName, 0, 0);
        WriteMember(
            buffer, memberSize, pointerSize, real32Member, 10, real32Offset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, formatMemberPlaceholder, 12, formatOffset, 0, 0);
        WriteMember(buffer, memberSize, pointerSize, degreeMemberPlaceholder, 12, degreeOffset, 0, 0);
        WriteMember(
            buffer, memberSize, pointerSize, curveHeaderMember, 1, curveHdrNameOffset, curveFormatDefinitionOffset, 0);
        WriteMember(buffer, memberSize, pointerSize, curvePaddingMember, 15, paddingOffsetName2, 0, 0);
        WriteMember(
            buffer, memberSize, pointerSize, curveKnotsMember, 3, knotsOffsetName, real32DefinitionOffset, 0);
        WriteMember(
            buffer, memberSize, pointerSize, curveControlsMember, 3, controlsOffsetName, real32DefinitionOffset, 0);
        WriteMember(buffer, memberSize, pointerSize, curveMember, 5, curveNameOffset, 0, 0);

        var sectionData = buffer.ToArray();
        return AssembleFile(
            pointerSize == 4 ? Signature32 : Signature,
            sectionData,
            relocations,
            rootDefinitionOffset,
            rootObjectOffset);
    }

    private static void AddTerminator(List<byte> buffer, int memberSize)
    {
        for (var i = 0; i < memberSize; i++)
        {
            buffer.Add(0);
        }
    }

    private static void WriteMember(
        List<byte> buffer,
        int memberSize,
        int pointerSize,
        long memberOffset,
        int type,
        long nameOffset,
        long referencedTypeOffset,
        uint arraySize)
    {
        WriteUInt32(buffer, memberOffset, unchecked((uint)type));
        WritePointer(buffer, memberOffset + 4, pointerSize, (ulong)nameOffset);
        WritePointer(buffer, memberOffset + 4 + pointerSize, pointerSize, (ulong)referencedTypeOffset);
        WriteUInt32(buffer, memberOffset + 4 + pointerSize * 2, arraySize);
    }

    private static byte[] AssembleFile(
        byte[] signature,
        byte[] sectionData,
        List<(uint Position, uint TargetOffset)> relocations,
        long rootDefinitionOffset,
        long rootObjectOffset)
    {
        const long tableOffset = 0x68;
        var dataStart = tableOffset + 44;
        var relocationsOffset = dataStart + sectionData.Length;
        var marshallingOffset = relocationsOffset + relocations.Count * 12L;
        var fileSize = marshallingOffset;

        var file = new byte[fileSize];
        signature.CopyTo(file, 0);
        WriteUInt32(file, 0x10, (uint)dataStart);
        WriteUInt32(file, 0x14, 0);
        WriteUInt32(file, 0x20, 7);
        WriteUInt32(file, 0x24, (uint)fileSize);
        WriteUInt32(file, 0x2C, 0x48);
        WriteUInt32(file, 0x30, 1);
        WriteUInt32(file, 0x34, 0);
        WriteUInt32(file, 0x38, (uint)rootDefinitionOffset);
        WriteUInt32(file, 0x3C, 0);
        WriteUInt32(file, 0x40, (uint)rootObjectOffset);
        WriteUInt32(file, 0x44, 0x80000039);

        WriteUInt32(file, tableOffset + 0, 0);
        WriteUInt32(file, tableOffset + 4, (uint)dataStart);
        WriteUInt32(file, tableOffset + 8, (uint)sectionData.Length);
        WriteUInt32(file, tableOffset + 12, (uint)sectionData.Length);
        WriteUInt32(file, tableOffset + 16, 0);
        WriteUInt32(file, tableOffset + 20, 0);
        WriteUInt32(file, tableOffset + 24, 0);
        WriteUInt32(file, tableOffset + 28, (uint)relocationsOffset);
        WriteUInt32(file, tableOffset + 32, (uint)relocations.Count);
        WriteUInt32(file, tableOffset + 36, (uint)marshallingOffset);
        WriteUInt32(file, tableOffset + 40, 0);

        sectionData.CopyTo(file, dataStart);

        var position = relocationsOffset;
        foreach (var relocation in relocations)
        {
            WriteUInt32(file, position, relocation.Position);
            WriteUInt32(file, position + 4, 0);
            WriteUInt32(file, position + 8, relocation.TargetOffset);
            position += 12;
        }

        return file;
    }

    private static void WriteUInt32(List<byte> buffer, long offset, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 4; i++)
        {
            buffer[(int)offset + i] = bytes[i];
        }
    }

    private static void WritePointer(List<byte> buffer, long offset, int pointerSize, ulong value)
    {
        if (pointerSize == 4)
        {
            WriteUInt32(buffer, offset, unchecked((uint)value));
            return;
        }

        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 8; i++)
        {
            buffer[(int)offset + i] = bytes[i];
        }
    }

    private static void WriteUInt32(byte[] buffer, long offset, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 4; i++)
        {
            buffer[offset + i] = bytes[i];
        }
    }

    private static void WriteUInt64(byte[] buffer, long offset, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 8; i++)
        {
            buffer[offset + i] = bytes[i];
        }
    }
}
