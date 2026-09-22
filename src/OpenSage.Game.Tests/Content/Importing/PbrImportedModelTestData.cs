using System;
using System.IO;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenSage.Tests.Content.Importing;

/// <summary>
/// Synthetic PBR mesh/payload builders shared by the C3 materialization tests. All images are
/// generated in memory; nothing loads from the game installation.
/// </summary>
internal static class PbrImportedModelTestData
{
    public static byte[] EncodePng(byte r = 255, byte g = 0, byte b = 0)
    {
        using var image = new Image<Rgba32>(2, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    public static ImportedMaterial CreateFullRoleMaterial(bool embedded)
    {
        var png = EncodePng();

        ImportedTextureBinding Bind(string name, int uvSet, ImportedTextureRole role)
        {
            var source = embedded
                ? ImportedImageSource.CreateEmbeddedBytes(png, "image/png", $"helmet.glb#{name}")
                : ImportedImageSource.CreateVirtualPath($"textures/{name}.png");
            return new ImportedTextureBinding(source, uvSet, new ImportedSampler(), role);
        }

        return ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(
                baseColorTexture: Bind("base", 0, ImportedTextureRole.BaseColor),
                metallicRoughnessTexture: Bind("metallicRoughness", 0, ImportedTextureRole.MetallicRoughness),
                normalTexture: Bind("normal", 1, ImportedTextureRole.Normal),
                occlusionTexture: Bind("occlusion", 0, ImportedTextureRole.Occlusion),
                emissiveTexture: Bind("emissive", 1, ImportedTextureRole.Emissive)));
    }

    public static ImportedMesh CreateQuadMesh(
        ImportedMaterial material,
        bool skinned = false,
        bool withTangents = false,
        bool withUv1 = true,
        bool blendEnabled = false)
    {
        var vertices = new ImportedVertex[4];
        for (var i = 0; i < 4; i++)
        {
            var position = i switch
            {
                0 => new Vector3(0, 0, 0),
                1 => new Vector3(1, 0, 0),
                2 => new Vector3(1, 1, 0),
                _ => new Vector3(0, 1, 0),
            };
            var tangent = withTangents ? Vector3.UnitX : (Vector3?)null;
            var binormal = withTangents ? Vector3.UnitY : (Vector3?)null;

            vertices[i] = skinned
                ? new ImportedVertex(
                    position,
                    Vector3.UnitZ,
                    tangent: tangent,
                    binormal: binormal,
                    boneIndex0: 0,
                    boneIndex1: 1,
                    boneWeight0: 0.7f,
                    boneWeight1: 0.3f)
                : new ImportedVertex(position, Vector3.UnitZ, tangent: tangent, binormal: binormal);
        }

        var uv0 = new[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1),
        };
        var uv1 = withUv1
            ? new[]
            {
                new Vector2(0.25f, 0.25f),
                new Vector2(0.75f, 0.25f),
                new Vector2(0.75f, 0.75f),
                new Vector2(0.25f, 0.75f),
            }

            : null;

        return new ImportedMesh(
            vertices,
            [0, 1, 2, 0, 2, 3],
            new AxisAlignedBoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 0)),
            new BoundingSphere(new Vector3(0.5f, 0.5f, 0), 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: skinned,
            materials: [material],
            meshParts:
            [
                new ImportedMeshPart(
                    uv0,
                    uv1,
                    startIndex: 0,
                    indexCount: 6,
                    blendEnabled,
                    materialIndex: 0),
            ]);
    }

    public static ImportedModel CreateModel(ImportedMesh mesh, int boneCount = 2)
    {
        var bones = new ImportedBone[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            bones[i] = new ImportedBone($"Bone{i}", i - 1, Vector3.Zero, Quaternion.Identity);
        }

        return new ImportedModel(
            "PbrTestModel",
            new ImportedSkeleton("PbrTestSkeleton", bones),
            [new ImportedSubObject("PbrContainer.Quad", "Quad", 0, mesh)],
            ImportedSkeletonOrigin.Embedded);
    }
}
