#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// One converted source primitive: split runtime meshes sharing one material and attachment.
/// </summary>
internal sealed class GltfConvertedPrimitive
{
    public IReadOnlyList<ImportedMesh> Meshes { get; }

    public int MaterialIndex { get; }

    public int SourceNodeIndex { get; }

    public GltfConvertedPrimitive(IReadOnlyList<ImportedMesh> meshes, int materialIndex, int sourceNodeIndex)
    {
        Meshes = meshes;
        MaterialIndex = materialIndex;
        SourceNodeIndex = sourceNodeIndex;
    }
}

/// <summary>
/// Converts one supported triangle primitive into engine-owned converted vertices (basis +
/// units + attachment geometry transform applied), split into runtime-sized meshes with
/// per-mesh bounds. Materials are resolved through the supplied factory.
/// </summary>
internal static class GltfPrimitiveConverter
{
    public delegate ImportedMaterial MaterialFactory(Material? gltfMaterial);

    public static GltfConvertedPrimitive Convert(
        MeshPrimitive primitive,
        GltfAttachmentPlan attachment,
        GltfImportOptions options,
        MaterialFactory materialFactory)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(materialFactory);

        ValidatePrimitive(primitive);

        var positions = DecodeRequired(primitive, "POSITION", GltfAccessorDecoder.CopyPositions);
        var normals = DecodeRequired(primitive, "NORMAL", GltfAccessorDecoder.CopyNormals);

        System.Numerics.Vector3[]? tangents = null;
        float[]? handedness = null;
        if (primitive.VertexAccessors.TryGetValue("TANGENT", out var tangentAccessor))
        {
            (tangents, handedness) = GltfAccessorDecoder.CopyTangents(tangentAccessor, "TANGENT");
            ValidateMatchingCount(tangents.Length, positions.Length, "TANGENT");
        }

        System.Numerics.Vector2[]? uv0 = null;
        if (primitive.VertexAccessors.TryGetValue("TEXCOORD_0", out var uv0Accessor))
        {
            uv0 = GltfAccessorDecoder.CopyTextureCoordinates(uv0Accessor, "TEXCOORD_0");
            ValidateMatchingCount(uv0.Length, positions.Length, "TEXCOORD_0");
        }

        System.Numerics.Vector2[]? uv1 = null;
        if (primitive.VertexAccessors.TryGetValue("TEXCOORD_1", out var uv1Accessor))
        {
            uv1 = GltfAccessorDecoder.CopyTextureCoordinates(uv1Accessor, "TEXCOORD_1");
            ValidateMatchingCount(uv1.Length, positions.Length, "TEXCOORD_1");
        }

        var triangles = GltfAccessorDecoder.CopyTriangles(primitive, positions.Length);

        var converted = ConvertVertices(
            positions,
            normals,
            tangents,
            handedness,
            uv0,
            uv1,
            attachment,
            options);

        var splits = GltfMeshSplitter.Split(converted, triangles);

        var meshes = new List<ImportedMesh>(splits.Count);
        foreach (var split in splits)
        {
            meshes.Add(BuildMesh(split, uv0, uv1, materialFactory(primitive.Material), isSkinned: false));
        }

        return new GltfConvertedPrimitive(meshes, primitive.Material?.LogicalIndex ?? -1, attachment.SourceNodeIndex);
    }

    private static void ValidatePrimitive(MeshPrimitive primitive)
    {
        if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
        {
            throw new NotSupportedException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' uses unsupported mode " +
                $"'{primitive.DrawPrimitiveType}'; only triangles are supported.");
        }

        if (primitive.MorphTargetsCount > 0)
        {
            throw new NotSupportedException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' uses morph targets, which are outside the supported subset.");
        }

        if (primitive.IndexAccessor is null &&
            primitive.VertexAccessors.TryGetValue("POSITION", out var positionAccessor) &&
            positionAccessor.Count % 3 != 0)
        {
            throw new InvalidDataException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' has {positionAccessor.Count} vertices " +
                "without an index buffer; non-indexed triangle counts must be divisible by three.");
        }

        foreach (var attribute in primitive.VertexAccessors.Keys)
        {
            if (attribute is "POSITION" or "NORMAL" or "TANGENT" or "TEXCOORD_0" or "TEXCOORD_1")
            {
                continue;
            }

            throw new NotSupportedException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' uses unsupported attribute '{attribute}'.");
        }
    }

    private static T[] DecodeRequired<T>(MeshPrimitive primitive, string attribute, Func<Accessor, string, T[]> decode)
    {
        if (!primitive.VertexAccessors.TryGetValue(attribute, out var accessor))
        {
            throw new InvalidDataException(
                $"Primitive of mesh '{primitive.LogicalParent.Name}' is missing required attribute '{attribute}'.");
        }

        var result = decode(accessor, attribute);
        if (result.Length == 0)
        {
            throw new InvalidDataException(
                $"Attribute '{attribute}' of mesh '{primitive.LogicalParent.Name}' has no values.");
        }

        return result;
    }

    private static void ValidateMatchingCount(int attributeCount, int vertexCount, string attributeName)
    {
        if (attributeCount != vertexCount)
        {
            throw new InvalidDataException(
                $"Attribute '{attributeName}' has {attributeCount} values, but POSITION has {vertexCount}.");
        }
    }

    private static ImportedVertex[] ConvertVertices(
        System.Numerics.Vector3[] positions,
        System.Numerics.Vector3[] normals,
        System.Numerics.Vector3[]? tangents,
        float[]? handedness,
        System.Numerics.Vector2[]? uv0,
        System.Numerics.Vector2[]? uv1,
        GltfAttachmentPlan attachment,
        GltfImportOptions options)
    {
        var basis = GltfTransformConverter.CreateBasis(options.UnitsPerMeter);
        var vertices = new ImportedVertex[positions.Length];

        for (var i = 0; i < positions.Length; i++)
        {
            // Geometry scale applies in glTF space first, then the basis conversion.
            var position = GltfTransformConverter.ConvertPosition(
                Vector3.Transform(positions[i], attachment.GeometryTransform),
                basis);

            var normal = Vector3.Normalize(GltfTransformConverter.ConvertDirection(
                Vector3.Transform(normals[i], attachment.NormalMatrix)));

            Vector3? tangent = null;
            Vector3? binormal = null;
            if (tangents is not null && handedness is not null)
            {
                var rawTangent = Vector3.Normalize(Vector3.Transform(tangents[i], attachment.NormalMatrix));
                var convertedTangent = Vector3.Normalize(GltfTransformConverter.ConvertDirection(rawTangent));

                if (attachment.RequiresTangentReorthogonalization)
                {
                    convertedTangent = Vector3.Normalize(
                        convertedTangent - normal * Vector3.Dot(normal, convertedTangent));
                }

                tangent = convertedTangent;
                binormal = Vector3.Normalize(Vector3.Cross(normal, convertedTangent)) * handedness[i];
            }

            vertices[i] = new ImportedVertex(position, normal, tangent: tangent, binormal: binormal);
        }

        return vertices;
    }

    internal static ImportedMesh BuildMesh(
        GltfMeshSplitter.MeshSplit split,
        Vector2[]? uv0,
        Vector2[]? uv1,
        ImportedMaterial material,
        bool isSkinned)
    {
        var indices = new ushort[split.OriginalIndices.Count];
        for (var i = 0; i < split.OriginalIndices.Count; i++)
        {
            indices[i] = (ushort)split.OriginalIndices[i];
        }

        var splitPositions = new Vector3[split.Vertices.Count];
        for (var i = 0; i < split.Vertices.Count; i++)
        {
            splitPositions[i] = split.Vertices[i].Position;
        }

        var boundingBox = GltfBoundsCalculator.ComputeBox(splitPositions);
        var boundingSphere = GltfBoundsCalculator.ComputeSphere(splitPositions);

        var splitUv0 = CopySplitUv(uv0, split);
        var splitUv1 = CopySplitUv(uv1, split);

        var meshPart = new ImportedMeshPart(
            splitUv0,
            splitUv1,
            startIndex: 0,
            indexCount: (uint)indices.Length,
            blendEnabled: false,
            materialIndex: 0);

        return new ImportedMesh(
            split.Vertices,
            indices,
            boundingBox,
            boundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: isSkinned,
            [material],
            [meshPart]);
    }

    private static Vector2[]? CopySplitUv(Vector2[]? uv, GltfMeshSplitter.MeshSplit split)
    {
        if (uv is null)
        {
            return null;
        }

        var result = new Vector2[split.SourceVertexIndices.Count];
        for (var i = 0; i < split.SourceVertexIndices.Count; i++)
        {
            result[i] = uv[split.SourceVertexIndices[i]];
        }

        return result;
    }
}
