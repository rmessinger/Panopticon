#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// Deterministic CPU tangent generation for normal-mapped imported meshes that provide indexed
/// triangles, normals, and the selected UV sets but omit tangents or binormals. Accumulates
/// Lengyel triangle contributions in stable index order, keeps provided source tangents
/// (orthonormalized), and derives a handed binormal so mirrored UVs keep their handedness.
/// Returns a new immutable mesh; caller-owned data is never mutated. Failures throw contextual
/// <see cref="NotSupportedException"/> rather than disabling normal mapping.
/// </summary>
internal static class ImportedTangentGenerator
{
    private const float DeterminantEpsilon = 1e-12f;
    private const float ZeroLengthEpsilon = 1e-12f;

    public static ImportedMesh GenerateTangents(ImportedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var vertexCount = mesh.Vertices.Count;
        var tangentSums = new Vector3[vertexCount];
        var bitangentSums = new Vector3[vertexCount];
        var contributed = new bool[vertexCount];

        // Selected UV value per shared vertex, owned by the first normal-mapped part to use it.
        var selectedUv = new Vector2[vertexCount];
        var uvOwnerSet = new int[vertexCount];
        var uvAssigned = new bool[vertexCount];

        for (var partIndex = 0; partIndex < mesh.MeshParts.Count; partIndex++)
        {
            var part = mesh.MeshParts[partIndex];
            var payload = mesh.Materials[part.MaterialIndex].PbrMetallicRoughness;
            if (payload?.NormalTexture is null)
            {
                continue;
            }

            var uv = (payload.NormalTexture.UvSet == 0 ? part.TextureCoordinates0 : part.TextureCoordinates1)
                ?? throw new NotSupportedException(
                    $"Mesh part {partIndex} is normal-mapped but does not supply UV set {payload.NormalTexture.UvSet}.");
            if (uv.Count != vertexCount)
            {
                throw new NotSupportedException(
                    $"Mesh part {partIndex} UV set {payload.NormalTexture.UvSet} has {uv.Count} entries, but the mesh has {vertexCount} vertices.");
            }

            for (var i = part.StartIndex; i < part.StartIndex + part.IndexCount; i += 3)
            {
                var i0 = mesh.Indices[(int)i];
                var i1 = mesh.Indices[(int)(i + 1)];
                var i2 = mesh.Indices[(int)(i + 2)];

                foreach (var vertexIndex in new[] { i0, i1, i2 })
                {
                    if (uvAssigned[vertexIndex])
                    {
                        if (uvOwnerSet[vertexIndex] != partIndex
                            && selectedUv[vertexIndex] != uv[vertexIndex])
                        {
                            throw new NotSupportedException(
                                $"Vertex {vertexIndex} is shared between normal-mapped mesh parts with conflicting UV values; tangent generation would depend on part order.");
                        }
                    }
                    else
                    {
                        selectedUv[vertexIndex] = uv[vertexIndex];
                        uvOwnerSet[vertexIndex] = partIndex;
                        uvAssigned[vertexIndex] = true;
                    }
                }

                AccumulateTriangle(
                    mesh,
                    i0, i1, i2,
                    uv,
                    tangentSums,
                    bitangentSums,
                    contributed,
                    partIndex,
                    (int)i);
            }
        }

        var vertices = new ImportedVertex[vertexCount];
        var generatedAny = false;
        for (var v = 0; v < vertexCount; v++)
        {
            var vertex = mesh.Vertices[v];
            if (!contributed[v])
            {
                vertices[v] = vertex;
                continue;
            }

            var normal = vertex.Normal;
            if (!float.IsFinite(normal.X))
            {
                throw new NotSupportedException($"Vertex {v} has a non-finite normal.");
            }

            // Keep a provided source tangent (Granny ships tangents but no binormals);
            // otherwise derive one from the accumulated UV gradients.
            var rawTangent = vertex.Tangent;
            if (rawTangent is null)
            {
                if (tangentSums[v].LengthSquared() < ZeroLengthEpsilon)
                {
                    throw new NotSupportedException(
                        $"Vertex {v} accumulated a zero-length tangent; UV gradients cancel out or inputs are degenerate.");
                }

                rawTangent = tangentSums[v];
            }

            // Gram-Schmidt orthonormalization against the source normal.
            var tangent = Vector3.Normalize(rawTangent.Value - normal * Vector3.Dot(normal, rawTangent.Value));

            // Handedness from the accumulated bitangent keeps mirrored UV regions consistent.
            var handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangentSums[v]) < 0.0f ? -1.0f : 1.0f;
            var binormal = Vector3.Cross(normal, tangent) * handedness;

            vertices[v] = new ImportedVertex(
                vertex.Position,
                vertex.Normal,
                vertex.SecondaryPosition,
                vertex.SecondaryNormal,
                tangent,
                binormal,
                vertex.BoneIndex0,
                vertex.BoneIndex1,
                vertex.BoneIndex2,
                vertex.BoneIndex3,
                vertex.BoneWeight0,
                vertex.BoneWeight1,
                vertex.BoneWeight2,
                vertex.BoneWeight3);
            generatedAny = true;
        }

        if (!generatedAny)
        {
            throw new NotSupportedException(
                "No normal-mapped triangle references any vertex; there is nothing to generate tangents from.");
        }

        return new ImportedMesh(
            vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            mesh.Hidden,
            mesh.CameraOriented,
            mesh.HasHouseColor,
            mesh.IsSkinned,
            mesh.Materials,
            mesh.MeshParts);
    }

    private static void AccumulateTriangle(
        ImportedMesh mesh,
        ushort i0,
        ushort i1,
        ushort i2,
        IReadOnlyList<Vector2> uv,
        Vector3[] tangentSums,
        Vector3[] bitangentSums,
        bool[] contributed,
        int partIndex,
        int indexOffset)
    {
        var p0 = mesh.Vertices[i0].Position;
        var p1 = mesh.Vertices[i1].Position;
        var p2 = mesh.Vertices[i2].Position;
        var uv0 = uv[i0];
        var uv1 = uv[i1];
        var uv2 = uv[i2];

        if (!float.IsFinite(p0.X) || !float.IsFinite(p1.X) || !float.IsFinite(p2.X)
            || !float.IsFinite(uv0.X) || !float.IsFinite(uv1.X) || !float.IsFinite(uv2.X))
        {
            throw new NotSupportedException(
                $"Mesh part {partIndex} triangle at index offset {indexOffset} has non-finite positions or UVs.");
        }

        var e1 = p1 - p0;
        var e2 = p2 - p0;
        var duv1 = uv1 - uv0;
        var duv2 = uv2 - uv0;

        var determinant = duv1.X * duv2.Y - duv1.Y * duv2.X;
        if (MathF.Abs(determinant) < DeterminantEpsilon)
        {
            throw new NotSupportedException(
                $"Mesh part {partIndex} triangle at index offset {indexOffset} has a degenerate UV mapping (zero area in UV space).");
        }

        var f = 1.0f / determinant;
        var tangent = new Vector3(
            f * (duv2.Y * e1.X - duv1.Y * e2.X),
            f * (duv2.Y * e1.Y - duv1.Y * e2.Y),
            f * (duv2.Y * e1.Z - duv1.Y * e2.Z));
        var bitangent = new Vector3(
            f * (duv1.X * e2.X - duv2.X * e1.X),
            f * (duv1.X * e2.Y - duv2.X * e1.Y),
            f * (duv1.X * e2.Z - duv2.X * e1.Z));

        tangentSums[i0] += tangent;
        bitangentSums[i0] += bitangent;
        tangentSums[i1] += tangent;
        bitangentSums[i1] += bitangent;
        tangentSums[i2] += tangent;
        bitangentSums[i2] += bitangent;
        contributed[i0] = true;
        contributed[i1] = true;
        contributed[i2] = true;
    }
}
