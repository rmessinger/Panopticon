#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Content.Importing;
using Panopticon.Graphics;

namespace Panopticon.Content.Loaders;

/// <summary>
/// CPU-only validation of the complete imported PBR material and mesh graph. Runs before any GPU
/// allocation or external-image access. Malformed data throws
/// <see cref="InvalidDataException"/>; valid but unsupported combinations throw
/// <see cref="NotSupportedException"/>. Non-PBR materials are untouched: their construction-time
/// invariants are unchanged, including the W3D missing-texture fallback.
/// </summary>
internal static class ImportedModelValidator
{
    public static void Validate(ImportedModel model, ModelBoneHierarchy hierarchy)
    {
        if (hierarchy.Bones.Length != model.Skeleton.Bones.Count)
        {
            throw new InvalidDataException(
                $"Model '{model.Name}' expects a skeleton with " +
                $"{model.Skeleton.Bones.Count} bones, but hierarchy " +
                $"'{hierarchy.Name}' has {hierarchy.Bones.Length}.");
        }

        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }

            ValidateMesh(model, subObject.FullName, mesh);
        }
    }

    private static void ValidateMesh(ImportedModel model, string subObjectName, ImportedMesh mesh)
    {
        // First normal-mapped part to use each shared vertex index, for UV conflict detection.
        var normalMappedVertexOwner = new Dictionary<int, int>();

        for (var partIndex = 0; partIndex < mesh.MeshParts.Count; partIndex++)
        {
            var part = mesh.MeshParts[partIndex];
            var material = mesh.Materials[part.MaterialIndex];
            var payload = material.PbrMetallicRoughness;
            if (payload is null)
            {
                continue;
            }

            var context = $"Model '{model.Name}' sub-object '{subObjectName}' mesh part {partIndex}";

            ValidateFactors(payload, context);

            var blendExpected = payload.AlphaMode == ImportedAlphaMode.Blend;
            if (part.BlendEnabled != blendExpected)
            {
                throw new InvalidDataException(
                    $"{context}: blend-enabled is {part.BlendEnabled}, but alpha mode " +
                    $"'{payload.AlphaMode}' requires {blendExpected}.");
            }

            var normalMapped = payload.NormalTexture is not null;
            foreach (var binding in AllBindings(payload))
            {
                if (binding is null)
                {
                    continue;
                }

                var uv = binding.UvSet == 0 ? part.TextureCoordinates0 : part.TextureCoordinates1;
                if (uv is null)
                {
                    throw new InvalidDataException(
                        $"{context}: {binding.Role} texture selects UV set {binding.UvSet}, which the mesh part does not supply.");
                }

                if (uv.Count != mesh.Vertices.Count)
                {
                    throw new InvalidDataException(
                        $"{context}: {binding.Role} texture UV set {binding.UvSet} has {uv.Count} entries, but the mesh has {mesh.Vertices.Count} vertices.");
                }
            }

            if (normalMapped)
            {
                ValidateNormalMappingInputs(mesh, part, partIndex, payload, context, normalMappedVertexOwner);
            }
        }
    }

    private static void ValidateFactors(ImportedPbrMetallicRoughness payload, string context)
    {
        ValidateFinite(payload.BaseColorFactor.X, "base color factor", context);
        ValidateFinite(payload.BaseColorFactor.Y, "base color factor", context);
        ValidateFinite(payload.BaseColorFactor.Z, "base color factor", context);
        ValidateFinite(payload.BaseColorFactor.W, "base color factor", context);

        ValidateFactorRange(payload.MetallicFactor, "metallic factor", context);
        ValidateFactorRange(payload.RoughnessFactor, "roughness factor", context);
        ValidateFactorRange(payload.OcclusionStrength, "occlusion strength", context);

        ValidateFinite(payload.NormalScale, "normal scale", context);
        ValidateFinite(payload.EmissiveFactor.X, "emissive factor", context);
        ValidateFinite(payload.EmissiveFactor.Y, "emissive factor", context);
        ValidateFinite(payload.EmissiveFactor.Z, "emissive factor", context);
        ValidateFinite(payload.AlphaCutoff, "alpha cutoff", context);
    }

    private static void ValidateFinite(float value, string name, string context)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"{context}: {name} must be finite, got {value}.");
        }
    }

    private static void ValidateFactorRange(float value, string name, string context)
    {
        ValidateFinite(value, name, context);
        if (value < 0.0f || value > 1.0f)
        {
            throw new InvalidDataException($"{context}: {name} must be within [0, 1], got {value}.");
        }
    }

    private static IEnumerable<ImportedTextureBinding?> AllBindings(ImportedPbrMetallicRoughness payload)
    {
        yield return payload.BaseColorTexture;
        yield return payload.MetallicRoughnessTexture;
        yield return payload.NormalTexture;
        yield return payload.OcclusionTexture;
        yield return payload.EmissiveTexture;
    }

    private static void ValidateNormalMappingInputs(
        ImportedMesh mesh,
        ImportedMeshPart part,
        int partIndex,
        ImportedPbrMetallicRoughness payload,
        string context,
        Dictionary<int, int> normalMappedVertexOwner)
    {
        var tangentCount = 0;
        foreach (var vertex in mesh.Vertices)
        {
            if (vertex.Tangent is { })
            {
                tangentCount++;
            }
        }

        if (tangentCount > 0 && tangentCount != mesh.Vertices.Count)
        {
            throw new InvalidDataException(
                $"{context}: {tangentCount} of {mesh.Vertices.Count} vertices carry tangents; a normal-mapped mesh needs a complete tangent basis or none at all.");
        }

        var uvSet = payload.NormalTexture!.UvSet;
        var uv = uvSet == 0 ? part.TextureCoordinates0 : part.TextureCoordinates1;
        if (tangentCount == 0 && (uv is null || uv.Count != mesh.Vertices.Count))
        {
            throw new NotSupportedException(
                $"{context}: normal mapping needs complete tangents or indexed triangles with normals and a complete UV set {uvSet} to generate them.");
        }

        // Normal-mapped parts sharing a vertex must agree on that vertex's UV in the selected
        // set; otherwise generated tangents would depend on part order.
        var owner = normalMappedVertexOwner;
        for (var i = part.StartIndex; i < part.StartIndex + part.IndexCount; i++)
        {
            var vertexIndex = mesh.Indices[(int)i];
            if (owner.TryGetValue(vertexIndex, out var firstPart))
            {
                if (firstPart != partIndex && !UVsEqual(mesh, firstPart, part, vertexIndex))
                {
                    throw new NotSupportedException(
                        $"{context}: vertex {vertexIndex} is shared with mesh part {firstPart}, but the parts' UV values for the vertex differ; tangent generation would depend on part order.");
                }
            }
            else
            {
                owner[vertexIndex] = partIndex;
            }
        }
    }

    private static bool UVsEqual(ImportedMesh mesh, int firstPartIndex, ImportedMeshPart secondPart, int vertexIndex)
    {
        var firstPart = mesh.MeshParts[firstPartIndex];

        // Both parts are normal-mapped PBR parts; their payloads were validated to have a
        // complete selected UV set before this point.
        var firstMaterial = mesh.Materials[firstPart.MaterialIndex].PbrMetallicRoughness!;
        var secondMaterial = mesh.Materials[secondPart.MaterialIndex].PbrMetallicRoughness!;
        var firstUvSet = firstMaterial.NormalTexture!.UvSet;
        var secondUvSet = secondMaterial.NormalTexture!.UvSet;

        var firstUv = (firstUvSet == 0 ? firstPart.TextureCoordinates0 : firstPart.TextureCoordinates1)!;
        var secondUv = (secondUvSet == 0 ? secondPart.TextureCoordinates0 : secondPart.TextureCoordinates1)!;

        return firstUv[vertexIndex] == secondUv[vertexIndex];
    }
}
