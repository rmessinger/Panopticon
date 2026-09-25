#nullable enable

using System;

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing;

public enum ImportedCullMode
{
    None,
    Back,
}

public enum ImportedDepthComparison
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always,
}

public enum ImportedBlendFactor
{
    Zero,
    One,
    SourceAlpha,
    InverseSourceAlpha,
    SourceColor,
    InverseSourceColor,
}

/// <summary>
/// CPU-only material description in engine semantics: shader family, render state, and optional
/// fixed-function shading values, plus an ordered named-property bag. No textures or GPU
/// objects; textures appear as texture-name property values.
/// </summary>
public sealed class ImportedMaterial
{
    public const string FixedFunctionFamily = "FixedFunction";

    public const string PbrMetallicRoughnessFamily = "PbrMetallicRoughness";

    public string ShaderFamily { get; }

    public ImportedCullMode CullMode { get; }

    public bool DepthWriteEnabled { get; }

    public ImportedDepthComparison DepthComparison { get; }

    public bool BlendEnabled { get; }

    public ImportedBlendFactor SourceBlendFactor { get; }

    public ImportedBlendFactor DestinationColorBlendFactor { get; }

    public ImportedBlendFactor DestinationAlphaBlendFactor { get; }

    public ImportedFixedFunctionShading? FixedFunctionShading { get; }

    public ImportedPbrMetallicRoughness? PbrMetallicRoughness { get; }

    public IReadOnlyList<ImportedMaterialProperty> Properties { get; }

    public ImportedMaterial(
        string shaderFamily,
        ImportedCullMode cullMode,
        bool depthWriteEnabled,
        ImportedDepthComparison depthComparison,
        bool blendEnabled,
        ImportedBlendFactor sourceBlendFactor,
        ImportedBlendFactor destinationColorBlendFactor,
        ImportedBlendFactor destinationAlphaBlendFactor,
        ImportedFixedFunctionShading? fixedFunctionShading,
        IReadOnlyList<ImportedMaterialProperty> properties,
        ImportedPbrMetallicRoughness? pbrMetallicRoughness = null)
    {
        if (string.IsNullOrEmpty(shaderFamily))
        {
            throw new ArgumentException("Shader family must not be null or empty.", nameof(shaderFamily));
        }

        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        for (var i = 0; i < properties.Count; i++)
        {
            if (properties[i] is null)
            {
                throw new ArgumentException($"Property at index {i} must not be null.", nameof(properties));
            }
        }

        var isPbrFamily = shaderFamily == PbrMetallicRoughnessFamily;
        if (isPbrFamily != (pbrMetallicRoughness is not null))
        {
            throw new ArgumentException(
                $"Shader family '{PbrMetallicRoughnessFamily}' and the PBR payload must appear together.",
                nameof(shaderFamily));
        }

        if (pbrMetallicRoughness is not null && fixedFunctionShading is not null)
        {
            throw new ArgumentException(
                "A material cannot combine a PBR payload with fixed-function shading.",
                nameof(fixedFunctionShading));
        }

        ShaderFamily = shaderFamily;
        CullMode = cullMode;
        DepthWriteEnabled = depthWriteEnabled;
        DepthComparison = depthComparison;
        BlendEnabled = blendEnabled;
        SourceBlendFactor = sourceBlendFactor;
        DestinationColorBlendFactor = destinationColorBlendFactor;
        DestinationAlphaBlendFactor = destinationAlphaBlendFactor;
        FixedFunctionShading = fixedFunctionShading;
        PbrMetallicRoughness = pbrMetallicRoughness;
        Properties = properties.ToArray();
    }

    /// <summary>
    /// Constructs a metallic-roughness material whose legacy render-state fields are derived from
    /// the payload: culling from double-sidedness, blending and depth-write from the alpha mode.
    /// </summary>
    public static ImportedMaterial CreatePbrMetallicRoughness(
        ImportedPbrMetallicRoughness payload,
        IReadOnlyList<ImportedMaterialProperty>? properties = null)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var blendEnabled = payload.AlphaMode == ImportedAlphaMode.Blend;

        return new ImportedMaterial(
            PbrMetallicRoughnessFamily,
            payload.DoubleSided ? ImportedCullMode.None : ImportedCullMode.Back,
            depthWriteEnabled: !blendEnabled,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: blendEnabled,
            sourceBlendFactor: blendEnabled ? ImportedBlendFactor.SourceAlpha : ImportedBlendFactor.One,
            destinationColorBlendFactor: blendEnabled ? ImportedBlendFactor.InverseSourceAlpha : ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: blendEnabled ? ImportedBlendFactor.InverseSourceAlpha : ImportedBlendFactor.Zero,
            fixedFunctionShading: null,
            pbrMetallicRoughness: payload,
            properties: properties ?? []);
    }
}
