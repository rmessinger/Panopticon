using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiTexturingProperty at 10.1.0.0: apply mode, texture count, and the
/// standard slot list. Only enabled slots carry descriptors; slot
/// presence follows the texture count (base..glow always, bump above 5,
/// decals 0..3 above 6..9).
/// </summary>
public sealed record NifTexturingProperty : NifProperty
{
    private static readonly string[] SlotNames =
    [
        "Base",
        "Dark",
        "Detail",
        "Gloss",
        "Glow",
        "BumpMap",
        "Decal0",
        "Decal1",
        "Decal2",
        "Decal3",
    ];

    public uint ApplyMode { get; }
    public uint TextureCount { get; }
    public IReadOnlyDictionary<string, NifTextureDescriptor> Slots { get; }

    private NifTexturingProperty(NifBinaryReader reader)
        : base(reader)
    {
        ApplyMode = reader.ReadUInt32("ApplyMode");
        if (ApplyMode > 4)
        {
            throw new InvalidDataException(
                $"NIF texturing property has apply mode {ApplyMode}; valid range is 0-4.");
        }
        TextureCount = reader.ReadUInt32("TextureCount");
        if (TextureCount > 9)
        {
            throw new InvalidDataException(
                $"NIF texturing property declares {TextureCount} textures; C4a reads at most 9 slots.");
        }

        var slots = new Dictionary<string, NifTextureDescriptor>();
        for (var i = 0; i < SlotNames.Length; i++)
        {
            if (i > 4 && TextureCount <= (uint)i - 1)
            {
                break;
            }
            var enabled = reader.ReadByte($"{SlotNames[i]}.Enabled");
            if (enabled != 0)
            {
                slots[SlotNames[i]] = NifTextureDescriptor.Read(reader, SlotNames[i]);
            }
        }
        Slots = slots;

        var numShaderTextures = reader.ReadUInt32("NumShaderTextures");
        if (numShaderTextures != 0)
        {
            throw new NotSupportedException(
                $"NIF texturing property declares {numShaderTextures} shader textures; C4a does not decode them.");
        }
    }

    internal static NifTexturingProperty Read(NifBinaryReader reader)
    {
        return new NifTexturingProperty(reader);
    }
}
