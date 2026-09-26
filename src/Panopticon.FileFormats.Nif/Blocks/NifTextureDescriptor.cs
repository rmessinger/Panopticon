using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// The static texture transform a texture slot may carry: translate,
/// tile, rotate around a center, expressed in UV space.
/// </summary>
public readonly record struct NifTextureTransform(
    float TranslationU,
    float TranslationV,
    float TilingU,
    float TilingV,
    float Rotation,
    uint TransformType,
    float CenterU,
    float CenterV);

/// <summary>
/// One texture stage descriptor at 10.1.0.0, including the optional
/// static texture transform.
/// </summary>
public sealed record NifTextureDescriptor(
    int SourceReference,
    uint ClampMode,
    uint FilterMode,
    uint UvSet,
    short Ps2L,
    short Ps2K,
    NifTextureTransform? Transform = null)
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
        NifTextureTransform? transform = null;
        if (hasTransform != 0)
        {
            var translationU = reader.ReadSingle($"{slotName}.Transform.TranslationU");
            var translationV = reader.ReadSingle($"{slotName}.Transform.TranslationV");
            var tilingU = reader.ReadSingle($"{slotName}.Transform.TilingU");
            var tilingV = reader.ReadSingle($"{slotName}.Transform.TilingV");
            var rotation = reader.ReadSingle($"{slotName}.Transform.Rotation");
            var transformType = reader.ReadUInt32($"{slotName}.Transform.TransformType");
            var centerU = reader.ReadSingle($"{slotName}.Transform.CenterU");
            var centerV = reader.ReadSingle($"{slotName}.Transform.CenterV");
            transform = new NifTextureTransform(
                translationU,
                translationV,
                tilingU,
                tilingV,
                rotation,
                transformType,
                centerU,
                centerV);
        }

        return new NifTextureDescriptor(source, clampMode, filterMode, uvSet, ps2L, ps2K, transform);
    }
}
