using System;
using System.Collections.Generic;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Member types of the Granny type tree. Numeric values are the on-disk
/// member type codes of 64-bit little-endian GR2 version 7 files.
/// </summary>
public enum GrannyMemberType
{
    None = 0,
    Inline = 1,
    Reference = 2,
    ReferenceToArray = 3,
    ArrayOfReferences = 4,
    VariantReference = 5,
    ReferenceToVariantArray = 7,
    String = 8,
    Transform = 9,
    Real32 = 10,
    Int8 = 11,
    UInt8 = 12,
    BinormalInt8 = 13,
    NormalUInt8 = 14,
    Int16 = 15,
    UInt16 = 16,
    BinormalInt16 = 17,
    NormalUInt16 = 18,
    Int32 = 19,
    UInt32 = 20,
    Real16 = 21,
    EmptyReference = 22,
}

/// <summary>
/// One member of a struct definition: a 44-byte entry in the file's type tree.
/// </summary>
public sealed class GrannyMemberDefinition
{
    public GrannyMemberType Type { get; init; }

    public string? Name { get; init; }

    /// <summary>Buffer offset of the referenced struct definition, when the type has one.</summary>
    public ulong ReferencedTypeOffset { get; init; }

    /// <summary>Element count for inline arrays; 0 for single elements.</summary>
    public uint ArraySize { get; init; }

    public uint Extra0 { get; init; }

    public uint Extra1 { get; init; }

    public uint Extra2 { get; init; }
}

/// <summary>
/// A struct definition: the member entries read until a terminating None entry.
/// </summary>
public sealed class GrannyStructDefinition
{
    public IReadOnlyList<GrannyMemberDefinition> Members { get; init; } = Array.Empty<GrannyMemberDefinition>();
}
