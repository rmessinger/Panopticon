#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing;

/// <summary>
/// CPU-only mesh: vertices, triangle indices, bounds, flags, ordered materials, and ordered mesh
/// parts. Skinning is the current two-bone layout; a non-skinned mesh must carry zero skin data.
/// </summary>
public sealed class ImportedMesh : ImportedRenderObject
{
    public IReadOnlyList<ImportedVertex> Vertices { get; }

    public IReadOnlyList<ushort> Indices { get; }

    public override AxisAlignedBoundingBox BoundingBox { get; }

    public override BoundingSphere BoundingSphere { get; }

    public bool Hidden { get; }

    public bool CameraOriented { get; }

    public bool HasHouseColor { get; }

    public bool IsSkinned { get; }

    public IReadOnlyList<ImportedMaterial> Materials { get; }

    public IReadOnlyList<ImportedMeshPart> MeshParts { get; }

    public string PrimitiveId { get; }

    public IReadOnlyList<ImportedMorphTargetSet> MorphTargetSets { get; }

    public ImportedMesh(
        IReadOnlyList<ImportedVertex> vertices,
        IReadOnlyList<ushort> indices,
        in AxisAlignedBoundingBox boundingBox,
        in BoundingSphere boundingSphere,
        bool hidden,
        bool cameraOriented,
        bool hasHouseColor,
        bool isSkinned,
        IReadOnlyList<ImportedMaterial> materials,
        IReadOnlyList<ImportedMeshPart> meshParts,
        string? primitiveId = null,
        IReadOnlyList<ImportedMorphTargetSet>? morphTargetSets = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        Ensure.NotEmpty(vertices, "A mesh must have at least one vertex.");

        if (vertices.Count > ushort.MaxValue)
            throw new ArgumentException(
                $"A mesh must have at most {ushort.MaxValue} vertices to match the runtime index format.",
                nameof(vertices));

        ArgumentNullException.ThrowIfNull(indices);

        Ensure.NotEmpty(indices, "A mesh must have at least one index.");

        if (indices.Count % 3 != 0)
            throw new ArgumentException("Index count must be a multiple of three.", nameof(indices));

        Ensure.AtLeast(boundingSphere.Radius, 0f, "Bounding sphere radius must not be negative.", nameof(boundingSphere));

        ArgumentNullException.ThrowIfNull(materials);

        Ensure.NotEmpty(materials, "A mesh must have at least one material.");

        ArgumentNullException.ThrowIfNull(meshParts);

        Ensure.NotEmpty(meshParts, "A mesh must have at least one mesh part.");

        var copiedVertices = CopyVertices(vertices, isSkinned);
        var copiedIndices = CopyIndices(indices, copiedVertices.Length);
        var copiedMorphTargetSets = CopyMorphTargetSets(morphTargetSets, copiedVertices.Length);

        Ensure.NoNullElements(materials, "Material");
        Ensure.NoNullElements(meshParts, "Mesh part");

        for (var i = 0; i < meshParts.Count; i++)
        {
            var meshPart = meshParts[i];

            if (meshPart.MaterialIndex >= materials.Count)
            {
                throw new ArgumentException(
                    $"Mesh part {i} references material {meshPart.MaterialIndex}, " +
                    $"but the mesh has {materials.Count} materials.",
                    nameof(meshParts));
            }

            if (meshPart.StartIndex + meshPart.IndexCount > (uint)copiedIndices.Length)
            {
                throw new ArgumentException(
                    $"Mesh part {i} range [{meshPart.StartIndex}, {meshPart.StartIndex + meshPart.IndexCount}) " +
                    $"exceeds the mesh's {copiedIndices.Length} indices.",
                    nameof(meshParts));
            }

            ValidateTextureCoordinates(meshPart, copiedVertices.Length, i, nameof(meshParts));
        }

        Vertices = copiedVertices;
        Indices = copiedIndices;
        BoundingBox = boundingBox;
        BoundingSphere = boundingSphere;
        Hidden = hidden;
        CameraOriented = cameraOriented;
        HasHouseColor = hasHouseColor;
        IsSkinned = isSkinned;
        Materials = materials.ToArray();
        MeshParts = meshParts.ToArray();
        PrimitiveId = primitiveId ?? string.Empty;
        MorphTargetSets = copiedMorphTargetSets;
    }

    private static ImportedMorphTargetSet[] CopyMorphTargetSets(
        IReadOnlyList<ImportedMorphTargetSet>? morphTargetSets,
        int vertexCount)
    {
        if (morphTargetSets is null)
        {
            return Array.Empty<ImportedMorphTargetSet>();
        }

        if (morphTargetSets.Count > 1)
            throw new ArgumentException(
                "A mesh carries at most one morph target set.",
                nameof(morphTargetSets));

        var copiedSets = new ImportedMorphTargetSet[morphTargetSets.Count];
        Ensure.NoNullElements(morphTargetSets, "Morph target set");

        for (var i = 0; i < morphTargetSets.Count; i++)
        {
            var set = morphTargetSets[i];

            for (var t = 0; t < set.Targets.Count; t++)
            {
                var target = set.Targets[t];
                ValidateDeltaCount(set, t, target.PositionDeltas, vertexCount, "position", nameof(morphTargetSets));
                ValidateDeltaCount(set, t, target.NormalDeltas, vertexCount, "normal", nameof(morphTargetSets));
                ValidateDeltaCount(set, t, target.TangentDeltas, vertexCount, "tangent", nameof(morphTargetSets));
            }

            copiedSets[i] = set;
        }

        return copiedSets;
    }

    private static void ValidateDeltaCount(
        ImportedMorphTargetSet set,
        int targetIndex,
        IReadOnlyList<Vector3>? deltas,
        int vertexCount,
        string kind,
        string paramName)
    {
        if (deltas is null)
        {
            return;
        }

        if (deltas.Count != vertexCount)
            throw new ArgumentException(
                $"Morph target set '{set.PrimitiveId}' target {targetIndex} has {deltas.Count} {kind} deltas, " +
                $"expected one per vertex ({vertexCount}).",
                paramName);
    }

    private static ImportedVertex[] CopyVertices(IReadOnlyList<ImportedVertex> vertices, bool isSkinned)
    {
        Ensure.NoNullElements(vertices, "Vertex");

        var copiedVertices = new ImportedVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];

            if (!isSkinned &&
                (vertex.BoneIndex0 != 0 || vertex.BoneIndex1 != 0 ||
                 vertex.BoneIndex2 != 0 || vertex.BoneIndex3 != 0 ||
                 vertex.BoneWeight0 != 0.0f || vertex.BoneWeight1 != 0.0f ||
                 vertex.BoneWeight2 != 0.0f || vertex.BoneWeight3 != 0.0f))
            {
                throw new ArgumentException(
                    $"Vertex {i} carries skinning data, but the mesh is not skinned.",
                    nameof(vertices));
            }

            if ((vertex.BoneWeight2 != 0.0f || vertex.BoneWeight3 != 0.0f) &&
                (vertex.SecondaryPosition is { } secondaryPosition &&
                     secondaryPosition != vertex.Position ||
                 vertex.SecondaryNormal is { } secondaryNormal &&
                     secondaryNormal != vertex.Normal))
            {
                throw new ArgumentException(
                    $"Vertex {i} has secondary geometry, but weights on slots 2 and 3 require " +
                    $"secondary geometry equal to the primary geometry.",
                    nameof(vertices));
            }

            copiedVertices[i] = vertex;
        }

        return copiedVertices;
    }

    private static ushort[] CopyIndices(IReadOnlyList<ushort> indices, int vertexCount)
    {
        var copiedIndices = new ushort[indices.Count];
        for (var i = 0; i < indices.Count; i++)
        {
            if (indices[i] >= vertexCount)
            {
                throw new ArgumentException(
                    $"Index {i} (value {indices[i]}) exceeds the mesh's {vertexCount} vertices.",
                    nameof(indices));
            }

            copiedIndices[i] = indices[i];
        }

        return copiedIndices;
    }

    private static void ValidateTextureCoordinates(
        ImportedMeshPart meshPart,
        int vertexCount,
        int partIndex,
        string paramName)
    {
        ValidateTextureCoordinateArray(meshPart.TextureCoordinates0, vertexCount, partIndex, 0, paramName);
        ValidateTextureCoordinateArray(meshPart.TextureCoordinates1, vertexCount, partIndex, 1, paramName);
    }

    private static void ValidateTextureCoordinateArray(
        IReadOnlyList<Vector2>? textureCoordinates,
        int vertexCount,
        int partIndex,
        int stage,
        string paramName)
    {
        if (textureCoordinates is null)
        {
            return;
        }

        if (textureCoordinates.Count != vertexCount)
            throw new ArgumentException(
                $"Mesh part {partIndex} texture stage {stage} has {textureCoordinates.Count} coordinate pairs, " +
                $"expected one per vertex ({vertexCount}).",
                paramName);
    }
}
