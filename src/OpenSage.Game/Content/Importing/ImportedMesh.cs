#nullable enable

using System;

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Mathematics;

namespace OpenSage.Content.Importing;

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

    public bool IsSkinned { get; }

    public IReadOnlyList<ImportedMaterial> Materials { get; }

    public IReadOnlyList<ImportedMeshPart> MeshParts { get; }

    public ImportedMesh(
        IReadOnlyList<ImportedVertex> vertices,
        IReadOnlyList<ushort> indices,
        in AxisAlignedBoundingBox boundingBox,
        in BoundingSphere boundingSphere,
        bool hidden,
        bool cameraOriented,
        bool isSkinned,
        IReadOnlyList<ImportedMaterial> materials,
        IReadOnlyList<ImportedMeshPart> meshParts)
    {
        if (vertices is null)
        {
            throw new ArgumentNullException(nameof(vertices));
        }

        if (vertices.Count == 0)
        {
            throw new ArgumentException("A mesh must have at least one vertex.", nameof(vertices));
        }

        if (vertices.Count > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"A mesh must have at most {ushort.MaxValue} vertices to match the runtime index format.",
                nameof(vertices));
        }

        if (indices is null)
        {
            throw new ArgumentNullException(nameof(indices));
        }

        if (indices.Count == 0)
        {
            throw new ArgumentException("A mesh must have at least one index.", nameof(indices));
        }

        if (indices.Count % 3 != 0)
        {
            throw new ArgumentException("Index count must be a multiple of three.", nameof(indices));
        }

        if (boundingSphere.Radius < 0)
        {
            throw new ArgumentException("Bounding sphere radius must not be negative.", nameof(boundingSphere));
        }

        if (materials is null)
        {
            throw new ArgumentNullException(nameof(materials));
        }

        if (materials.Count == 0)
        {
            throw new ArgumentException("A mesh must have at least one material.", nameof(materials));
        }

        if (meshParts is null)
        {
            throw new ArgumentNullException(nameof(meshParts));
        }

        if (meshParts.Count == 0)
        {
            throw new ArgumentException("A mesh must have at least one mesh part.", nameof(meshParts));
        }

        var copiedVertices = CopyVertices(vertices, isSkinned);
        var copiedIndices = CopyIndices(indices, copiedVertices.Length);

        for (var i = 0; i < materials.Count; i++)
        {
            if (materials[i] is null)
            {
                throw new ArgumentException($"Material at index {i} must not be null.", nameof(materials));
            }
        }

        for (var i = 0; i < meshParts.Count; i++)
        {
            var meshPart = meshParts[i];
            if (meshPart is null)
            {
                throw new ArgumentException($"Mesh part at index {i} must not be null.", nameof(meshParts));
            }

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
        IsSkinned = isSkinned;
        Materials = materials.ToArray();
        MeshParts = meshParts.ToArray();
    }

    private static ImportedVertex[] CopyVertices(IReadOnlyList<ImportedVertex> vertices, bool isSkinned)
    {
        var copiedVertices = new ImportedVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (vertex is null)
            {
                throw new ArgumentException($"Vertex at index {i} must not be null.", nameof(vertices));
            }

            if (!isSkinned &&
                (vertex.BoneIndex0 != 0 || vertex.BoneIndex1 != 0 ||
                 vertex.BoneWeight0 != 0.0f || vertex.BoneWeight1 != 0.0f))
            {
                throw new ArgumentException(
                    $"Vertex {i} carries skinning data, but the mesh is not skinned.",
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
        {
            throw new ArgumentException(
                $"Mesh part {partIndex} texture stage {stage} has {textureCoordinates.Count} coordinate pairs, " +
                $"expected one per vertex ({vertexCount}).",
                paramName);
        }
    }
}
