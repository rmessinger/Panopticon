using System;
using System.Collections.Generic;
using System.IO;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// One texture stage descriptor at 10.1.0.0. Texture transforms are out
/// of C4a scope and fail with NotSupportedException.
/// </summary>
public sealed record NifTextureDescriptor(
    int SourceReference,
    uint ClampMode,
    uint FilterMode,
    uint UvSet,
    short Ps2L,
    short Ps2K)
{
    internal static NifTextureDescriptor Read(NifBinaryReader reader, string slotName)
    {
        var source = reader.ReadBlockReference($"{slotName}.Source");
        var clampMode = reader.ReadUInt32($"{slotName}.ClampMode");
        if (clampMode > 3)
        {
            throw new InvalidDataException(
                $"NIF texture slot {slotName} has clamp mode {clampMode}; valid range is 0-3.");
        }

        var filterMode = reader.ReadUInt32($"{slotName}.FilterMode");
        if (filterMode > 6)
        {
            throw new InvalidDataException(
                $"NIF texture slot {slotName} has filter mode {filterMode}; valid range is 0-6.");
        }

        var uvSet = reader.ReadUInt32($"{slotName}.UvSet");
        var ps2L = reader.ReadInt16($"{slotName}.PS2L");
        var ps2K = reader.ReadInt16($"{slotName}.PS2K");
        var hasTransform = reader.ReadByte($"{slotName}.HasTextureTransform");
        if (hasTransform != 0)
        {
            throw new NotSupportedException(
                $"NIF texture slot {slotName} has a texture transform; C4a does not decode them.");
        }

        return new NifTextureDescriptor(source, clampMode, filterMode, uvSet, ps2L, ps2K);
    }
}
