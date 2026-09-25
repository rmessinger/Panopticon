#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.W3d;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing.W3d;

/// <summary>
/// Converts W3D mesh geometry into CPU-only interchange values: positions, normals, optional
/// secondary geometry, two-bone influences, indices, bounds, and mesh flags, composing the
/// material converter for parts and materials. No GPU or asset-store access.
/// </summary>
internal static class W3dMeshConverter
{
    public static ImportedMesh Convert(W3dMesh w3dMesh)
    {
        var vertices = ConvertVertices(w3dMesh);
        var indices = CreateIndices(w3dMesh);
        var materialConversion = W3dMaterialConverter.Convert(w3dMesh);

        return new ImportedMesh(
            vertices,
            indices,
            new AxisAlignedBoundingBox(w3dMesh.Header.Min, w3dMesh.Header.Max),
            new BoundingSphere(w3dMesh.Header.SphCenter, w3dMesh.Header.SphRadius),
            hidden: w3dMesh.Header.Attributes.HasFlag(W3dMeshFlags.Hidden),
            cameraOriented: (w3dMesh.Header.Attributes & W3dMeshFlags.GeometryTypeMask) == W3dMeshFlags.GeometryTypeCameraOriented,
            hasHouseColor: w3dMesh.Header.MeshName.StartsWith("HOUSECOLOR", StringComparison.Ordinal),
            isSkinned: w3dMesh.IsSkinned,
            materials: materialConversion.Materials,
            meshParts: materialConversion.MeshParts);
    }

    internal static ushort[] CreateIndices(W3dMesh w3dMesh)
    {
        var triangles = w3dMesh.Triangles.Items.AsSpan();
        var indices = new ushort[triangles.Length * 3];

        var indexIndex = 0;
        foreach (ref readonly var triangle in triangles)
        {
            if (triangle.VIndex0 > ushort.MaxValue
                || triangle.VIndex1 > ushort.MaxValue
                || triangle.VIndex2 > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Mesh '{w3dMesh.Header.MeshName}' has a triangle vertex index above the " +
                    "16-bit index limit.");
            }

            indices[indexIndex++] = (ushort)triangle.VIndex0;
            indices[indexIndex++] = (ushort)triangle.VIndex1;
            indices[indexIndex++] = (ushort)triangle.VIndex2;
        }

        return indices;
    }

    private static ImportedVertex[] ConvertVertices(W3dMesh w3dMesh)
    {
        var numVertices = (int)w3dMesh.Header.NumVertices;
        ValidateArrayLength(w3dMesh, w3dMesh.Vertices.Items.Length, numVertices, "vertices");
        ValidateArrayLength(w3dMesh, w3dMesh.Normals.Items.Length, numVertices, "normals");
        ValidateOptionalArrayLength(w3dMesh, w3dMesh.Vertices2, numVertices, "secondary vertices");
        ValidateOptionalArrayLength(w3dMesh, w3dMesh.Normals2, numVertices, "secondary normals");
        ValidateOptionalArrayLength(w3dMesh, w3dMesh.Tangents, numVertices, "tangents");
        ValidateOptionalArrayLength(w3dMesh, w3dMesh.Bitangents, numVertices, "binormals");
        if (w3dMesh.IsSkinned)
        {
            if (w3dMesh.Influences is null)
            {
                throw new InvalidDataException(
                    $"Skinned mesh '{w3dMesh.Header.MeshName}' has no vertex influences.");
            }

            ValidateArrayLength(w3dMesh, w3dMesh.Influences.Items.Length, numVertices, "influences");
        }

        var result = new ImportedVertex[numVertices];

        for (var i = 0; i < numVertices; i++)
        {
            var influence = w3dMesh.IsSkinned
                ? w3dMesh.Influences!.Items[i]
                : default;

            result[i] = new ImportedVertex(
                w3dMesh.Vertices.Items[i],
                w3dMesh.Normals.Items[i],
                secondaryPosition: GetValueOrNull(w3dMesh.Vertices2, i),
                secondaryNormal: GetValueOrNull(w3dMesh.Normals2, i),
                tangent: GetValueOrNull(w3dMesh.Tangents, i),
                binormal: GetValueOrNull(w3dMesh.Bitangents, i),
                boneIndex0: w3dMesh.IsSkinned ? (uint)influence.BoneIndex0 : 0,
                boneIndex1: w3dMesh.IsSkinned ? (uint)influence.BoneIndex1 : 0,
                boneWeight0: w3dMesh.IsSkinned ? influence.BoneWeight0 / 100.0f : 0.0f,
                boneWeight1: w3dMesh.IsSkinned ? influence.BoneWeight1 / 100.0f : 0.0f);
        }

        return result;
    }

    private static void ValidateArrayLength(W3dMesh w3dMesh, int actualLength, int expectedLength, string arrayName)
    {
        if (actualLength != expectedLength)
        {
            throw new InvalidDataException(
                $"Mesh '{w3dMesh.Header.MeshName}' has {actualLength} {arrayName}, expected " +
                $"{expectedLength} to match the vertex count.");
        }
    }

    private static void ValidateOptionalArrayLength(W3dMesh w3dMesh, W3dVector3List? list, int expectedLength, string arrayName)
    {
        if (list != null)
        {
            ValidateArrayLength(w3dMesh, list.Items.Length, expectedLength, arrayName);
        }
    }

    private static Vector3? GetValueOrNull(W3dVector3List? list, int index)
    {
        return list != null
            ? list.Items[index]
            : null;
    }
}
