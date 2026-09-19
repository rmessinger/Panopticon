#nullable enable

using System;
using System.Numerics;

namespace OpenSage.Content.Importing;

public enum ImportedAlphaMode
{
    Opaque,
    Mask,
    Blend,
}

/// <summary>
/// Core metallic-roughness material payload in engine semantics. Authoritative for culling,
/// blending, depth-write, and alpha-test behavior; legacy render-state fields on
/// <see cref="ImportedMaterial"/> are derived from it, never independently specified.
/// </summary>
public sealed class ImportedPbrMetallicRoughness
{
    public Vector4 BaseColorFactor { get; }

    public ImportedTextureBinding? BaseColorTexture { get; }

    public float MetallicFactor { get; }

    public float RoughnessFactor { get; }

    public ImportedTextureBinding? MetallicRoughnessTexture { get; }

    public ImportedTextureBinding? NormalTexture { get; }

    public float NormalScale { get; }

    public ImportedTextureBinding? OcclusionTexture { get; }

    public float OcclusionStrength { get; }

    public Vector3 EmissiveFactor { get; }

    public ImportedTextureBinding? EmissiveTexture { get; }

    public ImportedAlphaMode AlphaMode { get; }

    public float AlphaCutoff { get; }

    public bool DoubleSided { get; }

    public ImportedPbrMetallicRoughness(
        Vector4? baseColorFactor = null,
        ImportedTextureBinding? baseColorTexture = null,
        float metallicFactor = 1.0f,
        float roughnessFactor = 1.0f,
        ImportedTextureBinding? metallicRoughnessTexture = null,
        ImportedTextureBinding? normalTexture = null,
        float normalScale = 1.0f,
        ImportedTextureBinding? occlusionTexture = null,
        float occlusionStrength = 1.0f,
        Vector3? emissiveFactor = null,
        ImportedTextureBinding? emissiveTexture = null,
        ImportedAlphaMode alphaMode = ImportedAlphaMode.Opaque,
        float alphaCutoff = 0.5f,
        bool doubleSided = false)
    {
        ValidateRole(nameof(baseColorTexture), baseColorTexture, ImportedTextureRole.BaseColor);
        ValidateRole(nameof(metallicRoughnessTexture), metallicRoughnessTexture, ImportedTextureRole.MetallicRoughness);
        ValidateRole(nameof(normalTexture), normalTexture, ImportedTextureRole.Normal);
        ValidateRole(nameof(occlusionTexture), occlusionTexture, ImportedTextureRole.Occlusion);
        ValidateRole(nameof(emissiveTexture), emissiveTexture, ImportedTextureRole.Emissive);

        if (alphaCutoff is < 0.0f or > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(alphaCutoff), "Alpha cutoff must be within [0, 1].");
        }

        BaseColorFactor = baseColorFactor ?? Vector4.One;
        BaseColorTexture = baseColorTexture;
        MetallicFactor = metallicFactor;
        RoughnessFactor = roughnessFactor;
        MetallicRoughnessTexture = metallicRoughnessTexture;
        NormalTexture = normalTexture;
        NormalScale = normalScale;
        OcclusionTexture = occlusionTexture;
        OcclusionStrength = occlusionStrength;
        EmissiveFactor = emissiveFactor ?? Vector3.Zero;
        EmissiveTexture = emissiveTexture;
        AlphaMode = alphaMode;
        AlphaCutoff = alphaCutoff;
        DoubleSided = doubleSided;
    }

    private static void ValidateRole(string paramName, ImportedTextureBinding? binding, ImportedTextureRole expectedRole)
    {
        if (binding is { } && binding.Role != expectedRole)
        {
            throw new ArgumentException(
                $"Binding for '{paramName}' must use the {expectedRole} role, but it uses {binding.Role}.",
                paramName);
        }
    }
}
