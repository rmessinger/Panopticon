#nullable enable

using System;
using System.IO;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Maps glTF core metallic-roughness materials into the C3 interchange payload with glTF
/// defaults for omitted values. No SharpGLTF type crosses into C3 signatures.
/// </summary>
internal static class GltfMaterialConverter
{
    public static ImportedMaterial Convert(
        Material? material,
        GltfResourceResolver resolver)
    {
        if (material is null)
        {
            return ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness());
        }

        if (material.Unlit)
        {
            throw new NotSupportedException(
                $"glTF material '{material.Name}' uses KHR_materials_unlit, which is outside the supported subset.");
        }

        var metallicRoughness = material.FindChannel("MetallicRoughness");
        var baseColor = material.FindChannel("BaseColor");
        var normal = material.FindChannel("Normal");
        var occlusion = material.FindChannel("Occlusion");
        var emissive = material.FindChannel("Emissive");

        RejectTextureTransform(baseColor, material);
        RejectTextureTransform(metallicRoughness, material);
        RejectTextureTransform(normal, material);
        RejectTextureTransform(occlusion, material);
        RejectTextureTransform(emissive, material);

        var metallicFactor = FiniteOrDefault(metallicRoughness?.GetFactor("MetallicFactor"), 1f);
        var roughnessFactor = FiniteOrDefault(metallicRoughness?.GetFactor("RoughnessFactor"), 1f);

        var payload = new ImportedPbrMetallicRoughness(
            baseColorFactor: baseColor?.Color ?? System.Numerics.Vector4.One,
            baseColorTexture: ConvertBinding(baseColor, ImportedTextureRole.BaseColor, resolver),
            metallicFactor: metallicFactor,
            roughnessFactor: roughnessFactor,
            metallicRoughnessTexture: ConvertBinding(metallicRoughness, ImportedTextureRole.MetallicRoughness, resolver),
            normalTexture: ConvertBinding(normal, ImportedTextureRole.Normal, resolver),
            normalScale: FiniteOrDefault(normal?.GetFactor("NormalScale"), 1f),
            occlusionTexture: ConvertBinding(occlusion, ImportedTextureRole.Occlusion, resolver),
            occlusionStrength: FiniteOrDefault(occlusion?.GetFactor("OcclusionStrength"), 1f),
            emissiveFactor: emissive is { } emissiveChannel
                ? new System.Numerics.Vector3(emissiveChannel.Color.X, emissiveChannel.Color.Y, emissiveChannel.Color.Z)
                : System.Numerics.Vector3.Zero,
            emissiveTexture: ConvertBinding(emissive, ImportedTextureRole.Emissive, resolver),
            alphaMode: material.Alpha switch
            {
                AlphaMode.OPAQUE => ImportedAlphaMode.Opaque,
                AlphaMode.MASK => ImportedAlphaMode.Mask,
                AlphaMode.BLEND => ImportedAlphaMode.Blend,
                _ => throw new NotSupportedException($"glTF material '{material.Name}' uses unsupported alpha mode '{material.Alpha}'."),
            },
            alphaCutoff: material.AlphaCutoff,
            doubleSided: material.DoubleSided);

        return ImportedMaterial.CreatePbrMetallicRoughness(payload);
    }

    /// <summary>
    /// Rejects bindings whose UV set is not present on the consuming mesh part.
    /// </summary>
    public static void ValidateTextureBindingUvSets(
        ImportedMaterial material,
        bool hasTextureCoordinates0,
        bool hasTextureCoordinates1)
    {
        var payload = material.PbrMetallicRoughness;
        if (payload is null)
        {
            return;
        }

        ValidateBinding(payload.BaseColorTexture, hasTextureCoordinates0, hasTextureCoordinates1, material);
        ValidateBinding(payload.MetallicRoughnessTexture, hasTextureCoordinates0, hasTextureCoordinates1, material);
        ValidateBinding(payload.NormalTexture, hasTextureCoordinates0, hasTextureCoordinates1, material);
        ValidateBinding(payload.OcclusionTexture, hasTextureCoordinates0, hasTextureCoordinates1, material);
        ValidateBinding(payload.EmissiveTexture, hasTextureCoordinates0, hasTextureCoordinates1, material);
    }

    private static void ValidateBinding(
        ImportedTextureBinding? binding,
        bool hasTextureCoordinates0,
        bool hasTextureCoordinates1,
        ImportedMaterial material)
    {
        if (binding is null)
        {
            return;
        }

        var hasSelectedUv = binding.UvSet switch
        {
            0 => hasTextureCoordinates0,
            1 => hasTextureCoordinates1,
            _ => false,
        };

        if (!hasSelectedUv)
        {
            throw new InvalidDataException(
                $"Material of family '{material.ShaderFamily}' samples UV set {binding.UvSet}, " +
                "which the consuming mesh part does not provide.");
        }
    }

    private static void RejectTextureTransform(MaterialChannel? channel, Material material)
    {
        if (channel is { TextureTransform: not null })
        {
            throw new NotSupportedException(
                $"glTF material '{material.Name}' uses KHR_texture_transform, which is outside the supported subset.");
        }
    }

    private static ImportedTextureBinding? ConvertBinding(
        MaterialChannel? channel,
        ImportedTextureRole role,
        GltfResourceResolver resolver)
    {
        if (channel is not { } concreteChannel || concreteChannel.Texture is null)
        {
            return null;
        }

        var imageSource = GltfImageSourceConverter.Convert(concreteChannel.Texture.PrimaryImage, resolver);
        var sampler = ConvertSampler(concreteChannel.TextureSampler);

        if (concreteChannel.TextureCoordinate is < 0 or > 1)
        {
            throw new NotSupportedException(
                $"glTF material '{concreteChannel.LogicalParent.Name}' samples texture coordinate set " +
                $"{concreteChannel.TextureCoordinate}; only UV sets 0 and 1 are supported.");
        }

        return new ImportedTextureBinding(imageSource, concreteChannel.TextureCoordinate, sampler, role);
    }

    private static ImportedSampler ConvertSampler(TextureSampler? sampler)
    {
        // glTF defaults for a missing sampler or omitted filters.
        var addressU = ImportedAddressMode.Repeat;
        var addressV = ImportedAddressMode.Repeat;
        var minFilter = ImportedFilterMode.Linear;
        var mipFilter = ImportedFilterMode.Linear;
        var magFilter = ImportedFilterMode.Linear;

        if (sampler != null)
        {
            addressU = ConvertAddressMode(sampler.WrapS);
            addressV = ConvertAddressMode(sampler.WrapT);

            // Min filter selects both minification and mip filtering; mipmap variants keep
            // linear minification unless prefixed with NEAREST.
            minFilter = sampler.MinFilter switch
            {
                TextureMipMapFilter.NEAREST => ImportedFilterMode.Nearest,
                TextureMipMapFilter.NEAREST_MIPMAP_NEAREST => ImportedFilterMode.Nearest,
                TextureMipMapFilter.NEAREST_MIPMAP_LINEAR => ImportedFilterMode.Nearest,
                TextureMipMapFilter.LINEAR => ImportedFilterMode.Linear,
                TextureMipMapFilter.LINEAR_MIPMAP_NEAREST => ImportedFilterMode.Linear,
                TextureMipMapFilter.LINEAR_MIPMAP_LINEAR => ImportedFilterMode.Linear,
                _ => throw new NotSupportedException($"glTF sampler uses unsupported min filter '{sampler.MinFilter}'."),
            };

            mipFilter = sampler.MinFilter switch
            {
                TextureMipMapFilter.NEAREST_MIPMAP_NEAREST => ImportedFilterMode.Nearest,
                TextureMipMapFilter.LINEAR_MIPMAP_NEAREST => ImportedFilterMode.Nearest,
                _ => ImportedFilterMode.Linear,
            };

            magFilter = sampler.MagFilter switch
            {
                TextureInterpolationFilter.NEAREST => ImportedFilterMode.Nearest,
                TextureInterpolationFilter.LINEAR => ImportedFilterMode.Linear,
                _ => throw new NotSupportedException($"glTF sampler uses unsupported mag filter '{sampler.MagFilter}'."),
            };
        }

        return new ImportedSampler(addressU, addressV, minFilter, magFilter, mipFilter);
    }

    private static ImportedAddressMode ConvertAddressMode(TextureWrapMode mode)
    {
        return mode switch
        {
            TextureWrapMode.REPEAT => ImportedAddressMode.Repeat,
            TextureWrapMode.MIRRORED_REPEAT => ImportedAddressMode.MirroredRepeat,
            TextureWrapMode.CLAMP_TO_EDGE => ImportedAddressMode.Clamp,
            _ => throw new NotSupportedException($"glTF sampler uses unsupported wrap mode '{mode}'."),
        };
    }

    private static float FiniteOrDefault(float? value, float fallback)
    {
        return value is { } v && float.IsFinite(v) ? v : fallback;
    }
}
