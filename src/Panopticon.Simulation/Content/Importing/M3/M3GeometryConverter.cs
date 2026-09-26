#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.M3;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing.M3;

internal static class M3GeometryConverter
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    public static ImportedSubObject ConvertBatch(
        M3Model model,
        M3Division division,
        int batchIndex,
        string modelName,
        ImportedMaterial material)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (division is null)
        {
            throw new ArgumentNullException(nameof(division));
        }

        var batch = division.Batches[batchIndex];
        if (batch.Region >= division.Regions.Count)
        {
            throw new InvalidDataException(
                $"M3 batch {batchIndex} references region {batch.Region}, but the division has {division.Regions.Count} regions.");
        }

        var region = division.Regions[batch.Region];
        var vertices = DecodeVertices(model, region);
        var textureCoordinates0 = DecodeTextureCoordinates(model, region, uvSet: 0);
        var textureCoordinates1 = model.VertexFormat.HasUv1
            ? DecodeTextureCoordinates(model, region, uvSet: 1)
            : null;
        var indices = DecodeIndices(division, region, batchIndex);
        var (boundingBox, boundingSphere) = ComputeBounds(vertices);
        var meshPart = new ImportedMeshPart(
            textureCoordinates0,
            textureCoordinates1,
            startIndex: 0,
            (uint)indices.Length,
            material.BlendEnabled,
            materialIndex: 0);

        var mesh = new ImportedMesh(
            vertices,
            indices,
            boundingBox,
            boundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: [material],
            meshParts: [meshPart]);

        mesh = GenerateTangents(mesh, textureCoordinates0, textureCoordinates1);

        return new ImportedSubObject(
            $"{modelName}.batch{batchIndex}",
            $"batch{batchIndex}",
            parentBoneIndex: 0,
            mesh);
    }

    private static ImportedMesh GenerateTangents(
        ImportedMesh mesh,
        Vector2[] textureCoordinates0,
        Vector2[]? textureCoordinates1)
    {
        var temporaryNormalBinding = new ImportedTextureBinding(
            ImportedImageSource.CreateEmbeddedBytes(OnePixelPng, "image/png", "m3-placeholder-normal"),
            uvSet: 0,
            new ImportedSampler(),
            ImportedTextureRole.Normal);
        var temporaryMaterial = ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(normalTexture: temporaryNormalBinding));
        var temporaryPart = new ImportedMeshPart(
            textureCoordinates0,
            textureCoordinates1,
            startIndex: 0,
            (uint)mesh.Indices.Count,
            blendEnabled: false,
            materialIndex: 0);
        var temporaryMesh = new ImportedMesh(
            mesh.Vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            mesh.Hidden,
            mesh.CameraOriented,
            mesh.HasHouseColor,
            mesh.IsSkinned,
            [temporaryMaterial],
            [temporaryPart]);
        var tangentMesh = ImportedTangentGenerator.GenerateTangents(temporaryMesh);

        return new ImportedMesh(
            tangentMesh.Vertices,
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

    private static ImportedVertex[] DecodeVertices(M3Model model, M3Region region)
    {
        var vertices = new ImportedVertex[checked((int)region.VertexCount)];
        for (var i = 0; i < vertices.Length; i++)
        {
            var offset = checked(((int)region.FirstVertex + i) * model.VertexFormat.Stride);
            var position = ReadVector3(model.VertexBytes, offset);
            var normalOffset = offset + (model.VertexFormat.HasUv1 ? 28 : 24);
            var normal = DecodeNormal(model.VertexBytes, normalOffset);
            var engineNormal = M3CoordinateConverter.ToEngineDirection(normal);
            vertices[i] = new ImportedVertex(
                M3CoordinateConverter.ToEnginePosition(position),
                Vector3.Normalize(engineNormal));
        }

        return vertices;
    }

    private static Vector2[] DecodeTextureCoordinates(M3Model model, M3Region region, int uvSet)
    {
        var coordinates = new Vector2[checked((int)region.VertexCount)];
        var uvOffset = uvSet == 0 ? 20 : 24;
        for (var i = 0; i < coordinates.Length; i++)
        {
            var offset = checked(((int)region.FirstVertex + i) * model.VertexFormat.Stride + uvOffset);
            coordinates[i] = new Vector2(
                ReadInt16(model.VertexBytes, offset) / 2048.0f,
                ReadInt16(model.VertexBytes, offset + 2) / 2048.0f);
        }

        return coordinates;
    }

    private static ushort[] DecodeIndices(M3Division division, M3Region region, int batchIndex)
    {
        if (region.FaceIndexCount % 3 != 0)
        {
            throw new InvalidDataException(
                $"M3 batch {batchIndex} region face index count {region.FaceIndexCount} is not a multiple of three.");
        }

        if (region.FirstFaceIndex > division.Faces.Count ||
            region.FaceIndexCount > division.Faces.Count - region.FirstFaceIndex)
        {
            throw new InvalidDataException(
                $"M3 batch {batchIndex} region face range [{region.FirstFaceIndex}, {region.FirstFaceIndex + region.FaceIndexCount}) exceeds the division face count {division.Faces.Count}.");
        }

        var indices = new ushort[region.FaceIndexCount];
        for (var i = 0; i < indices.Length; i++)
        {
            var index = division.Faces[(int)region.FirstFaceIndex + i];
            if (index >= region.VertexCount)
            {
                throw new InvalidDataException(
                    $"M3 batch {batchIndex} face index {index} exceeds region vertex count {region.VertexCount}.");
            }

            indices[i] = index;
        }

        return indices;
    }

    private static (AxisAlignedBoundingBox Box, BoundingSphere Sphere) ComputeBounds(IReadOnlyList<ImportedVertex> vertices)
    {
        var positions = new Vector3[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            positions[i] = vertices[i].Position;
        }

        var boundingBox = AxisAlignedBoundingBox.CreateFromPoints(positions);
        var sphereCenter = (boundingBox.Min + boundingBox.Max) / 2.0f;
        var boundingSphere = new BoundingSphere(sphereCenter, Vector3.Distance(sphereCenter, boundingBox.Max));
        return (boundingBox, boundingSphere);
    }

    private static Vector3 ReadVector3(IReadOnlyList<byte> bytes, int offset)
    {
        return new Vector3(
            ReadSingle(bytes, offset),
            ReadSingle(bytes, offset + 4),
            ReadSingle(bytes, offset + 8));
    }

    private static Vector3 DecodeNormal(IReadOnlyList<byte> bytes, int offset)
    {
        return new Vector3(
            DecodeNormalComponent(bytes[offset]),
            DecodeNormalComponent(bytes[offset + 1]),
            DecodeNormalComponent(bytes[offset + 2]));
    }

    private static float DecodeNormalComponent(byte value)
    {
        return value / 255.0f * 2.0f - 1.0f;
    }

    private static short ReadInt16(IReadOnlyList<byte> bytes, int offset)
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer[0] = bytes[offset];
        buffer[1] = bytes[offset + 1];
        return BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    private static float ReadSingle(IReadOnlyList<byte> bytes, int offset)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = bytes[offset];
        buffer[1] = bytes[offset + 1];
        buffer[2] = bytes[offset + 2];
        buffer[3] = bytes[offset + 3];
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }
}
