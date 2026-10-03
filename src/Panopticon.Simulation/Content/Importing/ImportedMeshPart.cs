#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// One draw range of an imported mesh: per-vertex texture coordinates (null when the source has
/// none), an index range at a triangle boundary, a blend flag, and a material reference by index
/// into the owning mesh's ordered materials.
/// </summary>
public sealed class ImportedMeshPart
{
    public IReadOnlyList<Vector2>? TextureCoordinates0 { get; }

    public IReadOnlyList<Vector2>? TextureCoordinates1 { get; }

    public uint StartIndex { get; }

    public uint IndexCount { get; }

    public bool BlendEnabled { get; }

    public int MaterialIndex { get; }

    public ImportedMeshPart(
        Vector2[]? textureCoordinates0,
        Vector2[]? textureCoordinates1,
        uint startIndex,
        uint indexCount,
        bool blendEnabled,
        int materialIndex)
    {
        if (startIndex % 3 != 0)
            throw new ArgumentException("Part start index must fall on a triangle boundary.", nameof(startIndex));

        if (indexCount == 0 || indexCount % 3 != 0)
            throw new ArgumentException("Part index count must be a positive multiple of three.", nameof(indexCount));

        Ensure.AtLeast(materialIndex, 0, "Material index must not be negative.");

        TextureCoordinates0 = textureCoordinates0 == null ? null : (Vector2[])textureCoordinates0.Clone();
        TextureCoordinates1 = textureCoordinates1 == null ? null : (Vector2[])textureCoordinates1.Clone();
        StartIndex = startIndex;
        IndexCount = indexCount;
        BlendEnabled = blendEnabled;
        MaterialIndex = materialIndex;
    }
}
