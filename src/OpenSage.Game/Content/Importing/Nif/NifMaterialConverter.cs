#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Content.Importing;
using FileFormatsNif = OpenSage.FileFormats.Nif;

namespace OpenSage.Content.Importing.Nif;

/// <summary>
/// Maps the NIF fixed-function property set onto ImportedMaterial:
/// colors and specular state from NiMaterialProperty/NiSpecularProperty,
/// depth from NiZBufferProperty, alpha from NiAlphaProperty, and source
/// texture names as TextureName properties with texture-stage count zero
/// (C4a renders untextured diffuse; .texcache stays unresolved).
/// </summary>
internal static class NifMaterialConverter
{
    private const string Texture0PropertyName = "Texture0";

    internal static ImportedMaterial Convert(
        IReadOnlyList<FileFormatsNif.NifProperty> properties,
        IReadOnlyList<FileFormatsNif.NifBlock> blocks,
        string shapeName,
        AssetImportContext context)
    {
        var ambient = new Vector3(1, 1, 1);
        var diffuse = new Vector3(1, 1, 1);
        var specular = new Vector3(0, 0, 0);
        var emissive = new Vector3(0, 0, 0);
        var shininess = 0.0f;
        var opacity = 1.0f;
        var specularEnabled = false;
        var texturingEnabled = false;
        string? resolvedTexcachePath = null;
        var depthWriteEnabled = true;
        var depthComparison = ImportedDepthComparison.LessEqual;
        var blendEnabled = false;
        var sourceBlend = ImportedBlendFactor.One;
        var destinationBlend = ImportedBlendFactor.Zero;
        var alphaTest = false;

        var materialProperties = new List<ImportedMaterialProperty>();

        // Retained for diagnostics: where the cooked texture was found.
        if (resolvedTexcachePath is not null)
        {
            materialProperties.Add(ImportedMaterialProperty.FromBool(
                "HasTexcache", true));
        }

        foreach (var property in properties)
        {
            switch (property)
            {
                case FileFormatsNif.NifMaterialProperty material:
                    ambient = material.AmbientColor;
                    diffuse = material.DiffuseColor;
                    specular = material.SpecularColor;
                    emissive = material.EmissiveColor;
                    // NIF carries a 3ds Max glossiness (0-100 scale); the
                    // engine's fixed-function shininess multiplies the specular
                    // contribution directly, so a raw 10 would blow highlights
                    // out to full white across the mesh.
                    shininess = material.Glossiness / 100f;
                    opacity = material.Alpha;
                    break;

                case FileFormatsNif.NifSpecularProperty specularProperty:
                    specularEnabled = (specularProperty.Flags & 0x1) != 0;
                    break;

                case FileFormatsNif.NifZBufferProperty zBuffer:
                    depthWriteEnabled = (zBuffer.Flags & 0x2) != 0;
                    depthComparison = MapDepthComparison(zBuffer.Function);
                    break;

                case FileFormatsNif.NifAlphaProperty alpha:
                    blendEnabled = (alpha.Flags & 0x1) != 0;
                    if (blendEnabled)
                    {
                        sourceBlend = MapBlendFactor((uint)((alpha.Flags >> 1) & 0xF), shapeName);
                        destinationBlend = MapBlendFactor((uint)((alpha.Flags >> 5) & 0xF), shapeName);
                    }
                    alphaTest = (alpha.Flags & 0x200) != 0;
                    if (alphaTest)
                    {
                        materialProperties.Add(ImportedMaterialProperty.FromInt32(
                            "AlphaTestThreshold", alpha.Threshold));
                    }
                    break;

                case FileFormatsNif.NifTexturingProperty texturing:
                    if (texturing.Slots.TryGetValue("Base", out var baseSlot) &&
                        baseSlot.SourceReference >= 0 &&
                        blocks[baseSlot.SourceReference] is FileFormatsNif.NifSourceTexture sourceTexture)
                    {
                        materialProperties.Add(ImportedMaterialProperty.FromTextureName(
                            Texture0PropertyName, sourceTexture.FileName));
                        if (TryResolveTexcache(sourceTexture.FileName, context, out var texcachePath))
                        {
                            // Cooked texture available: render the base stage.
                            // The materializer resolves the name; when the
                            // entry is absent the C4a untextured fallback
                            // stays in force.
                            texturingEnabled = true;
                            resolvedTexcachePath = texcachePath;
                        }
                    }
                    break;

                case FileFormatsNif.NifVertexColorProperty:
                    // Source/lighting modes apply to vertex-color streams, which the
                    // parser already rejects; nothing to map.
                    break;
            }
        }

        var shading = new ImportedFixedFunctionShading(
            diffuseLightingType: ImportedDiffuseLightingType.Modulate,
            specularEnabled: specularEnabled,
            texturingEnabled: texturingEnabled,
            secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
            secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
            alphaTest: alphaTest,
            ambient: ambient,
            diffuse: diffuse,
            specular: specular,
            emissive: emissive,
            shininess: shininess,
            opacity: opacity,
            textureStageCount: texturingEnabled ? 1 : 0,
            textureMappingStage0: CreateIdentityMapping(),
            textureMappingStage1: CreateIdentityMapping());

        // EE2 Gamebryo winding does not match the engine's counter-clockwise
        // front-face convention (either orientation culls roughly the whole
        // mesh), so NIF materials import with culling disabled; both
        // orientations then render with correct lighting.
        return new ImportedMaterial(
            ImportedMaterial.FixedFunctionFamily,
            ImportedCullMode.None,
            depthWriteEnabled,
            depthComparison,
            blendEnabled,
            sourceBlend,
            destinationBlend,
            destinationBlend,
            shading,
            materialProperties);
    }

    /// <summary>
    /// Resolves the EE2 cooked-texture path for a source texture name
    /// (e.g. "nwg1_galley.tga" to "cache/nifcache" analog
    /// "cache/texcache/nwg1_galley.texcache") through the import file system.
    /// </summary>
    internal static bool TryResolveTexcache(
        string sourceFileName,
        AssetImportContext context,
        out string texcachePath)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(sourceFileName);
        texcachePath = $"cache/texcache/{stem}.texcache";
        return context.FileSystem.GetFile(texcachePath) is not null;
    }

    private static ImportedDepthComparison MapDepthComparison(uint function)
    {
        return function switch
        {
            0 => ImportedDepthComparison.Always,
            1 => ImportedDepthComparison.Less,
            2 => ImportedDepthComparison.Equal,
            3 => ImportedDepthComparison.LessEqual,
            4 => ImportedDepthComparison.Greater,
            5 => ImportedDepthComparison.NotEqual,
            6 => ImportedDepthComparison.GreaterEqual,
            7 => ImportedDepthComparison.Never,
            _ => throw new NotSupportedException(
                $"NIF z-buffer test function {function} has no mapping."),
        };
    }

    private static ImportedBlendFactor MapBlendFactor(uint alphaFunction, string shapeName)
    {
        return alphaFunction switch
        {
            0 => ImportedBlendFactor.One,
            1 => ImportedBlendFactor.Zero,
            2 => ImportedBlendFactor.SourceColor,
            3 => ImportedBlendFactor.InverseSourceColor,
            4 or 5 => throw new NotSupportedException(
                $"NIF shape '{shapeName}' uses destination-color blending, which has no " +
                $"ImportedBlendFactor mapping."),
            6 => ImportedBlendFactor.SourceAlpha,
            7 => ImportedBlendFactor.InverseSourceAlpha,
            _ => throw new NotSupportedException(
                $"NIF alpha blend function {alphaFunction} has no mapping."),
        };
    }

    private static ImportedTextureMapping CreateIdentityMapping()
    {
        return new ImportedTextureMapping(
            ImportedTextureMappingType.Uv,
            Vector2.Zero,
            Vector2.One,
            Vector2.Zero,
            Vector2.Zero,
            Vector2.Zero,
            Vector2.Zero,
            Vector2.Zero,
            speed: 0.0f,
            fps: 0.0f,
            log2Width: 0,
            stepsPerSecond: 0.0f);
    }
}
