#nullable enable

using System;
using System.Numerics;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedDiffuseLightingType
{
    Disable,
    Modulate,
    Add,
    BumpEnvMap,
}

public enum ImportedSecondaryTextureBlend
{
    Disable,
    Detail,
    Scale,
    InvScale,
    DetailBlend,
    Add,
}

/// <summary>
/// Fixed-function shading values mirroring the engine's current fixed-function material
/// constants. Null on materials that use a named shader family instead.
/// </summary>
public sealed class ImportedFixedFunctionShading
{
    public ImportedDiffuseLightingType DiffuseLightingType { get; }

    public bool SpecularEnabled { get; }

    public bool TexturingEnabled { get; }

    public ImportedSecondaryTextureBlend SecondaryTextureColorBlend { get; }

    public ImportedSecondaryTextureBlend SecondaryTextureAlphaBlend { get; }

    public bool AlphaTest { get; }

    public Vector3 Ambient { get; }

    public Vector3 Diffuse { get; }

    public Vector3 Specular { get; }

    public Vector3 Emissive { get; }

    public float Shininess { get; }

    public float Opacity { get; }

    public int TextureStageCount { get; }

    public ImportedTextureMapping TextureMappingStage0 { get; }

    public ImportedTextureMapping TextureMappingStage1 { get; }

    public ImportedFixedFunctionShading(
        ImportedDiffuseLightingType diffuseLightingType,
        bool specularEnabled,
        bool texturingEnabled,
        ImportedSecondaryTextureBlend secondaryTextureColorBlend,
        ImportedSecondaryTextureBlend secondaryTextureAlphaBlend,
        bool alphaTest,
        in Vector3 ambient,
        in Vector3 diffuse,
        in Vector3 specular,
        in Vector3 emissive,
        float shininess,
        float opacity,
        int textureStageCount,
        ImportedTextureMapping textureMappingStage0,
        ImportedTextureMapping textureMappingStage1)
    {
        Ensure.InRange(textureStageCount, 0, 2, "Texture stage count must be 0, 1, or 2.");

        DiffuseLightingType = diffuseLightingType;
        SpecularEnabled = specularEnabled;
        TexturingEnabled = texturingEnabled;
        SecondaryTextureColorBlend = secondaryTextureColorBlend;
        SecondaryTextureAlphaBlend = secondaryTextureAlphaBlend;
        AlphaTest = alphaTest;
        Ambient = ambient;
        Diffuse = diffuse;
        Specular = specular;
        Emissive = emissive;
        Shininess = shininess;
        Opacity = opacity;
        TextureStageCount = textureStageCount;
        TextureMappingStage0 = textureMappingStage0;
        TextureMappingStage1 = textureMappingStage1;
    }
}
