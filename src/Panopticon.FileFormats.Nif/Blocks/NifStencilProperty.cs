using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiStencilProperty at 10.1.0.0 (EE2 cooked): stencil flags, an enable
/// byte, and a 26-byte tail of stencil state this variant packs without a
/// known field grammar. Decoded structurally only; the property
/// participates in material gathering like any other property block and
/// stencil rendering is out of scope.
/// </summary>
public sealed record NifStencilProperty : NifProperty
{
    public ushort Flags { get; }

    public byte StencilEnabled { get; }

    public IReadOnlyList<byte> UnknownTail { get; }

    private NifStencilProperty(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
        StencilEnabled = reader.ReadByte("StencilEnabled");
        var tail = new byte[26];
        if (reader.ReadBytes(tail) != tail.Length)
        {
            throw new InvalidDataException(
                $"NIF stencil property block is truncated; expected a 26-byte tail.");
        }

        UnknownTail = tail;
    }

    internal static NifStencilProperty Read(NifBinaryReader reader)
    {
        return new NifStencilProperty(reader);
    }
}
