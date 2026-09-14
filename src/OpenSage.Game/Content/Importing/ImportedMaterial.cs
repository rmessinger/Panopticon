#nullable enable

using System;

using System.Collections.Generic;
using System.Linq;

namespace OpenSage.Content.Importing;

public enum ImportedCullMode
{
    None,
    Back,
}

public enum ImportedDepthComparison
{
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

    public string ShaderFamily { get; }

    public ImportedCullMode CullMode { get; }

    public bool DepthWriteEnabled { get; }

    public ImportedDepthComparison DepthComparison { get; }

    public bool BlendEnabled { get; }

    public ImportedBlendFactor SourceBlendFactor { get; }

    public ImportedBlendFactor DestinationColorBlendFactor { get; }

    public ImportedBlendFactor DestinationAlphaBlendFactor { get; }

    public ImportedFixedFunctionShading? FixedFunctionShading { get; }

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
        IReadOnlyList<ImportedMaterialProperty> properties)
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

        ShaderFamily = shaderFamily;
        CullMode = cullMode;
        DepthWriteEnabled = depthWriteEnabled;
        DepthComparison = depthComparison;
        BlendEnabled = blendEnabled;
        SourceBlendFactor = sourceBlendFactor;
        DestinationColorBlendFactor = destinationColorBlendFactor;
        DestinationAlphaBlendFactor = destinationAlphaBlendFactor;
        FixedFunctionShading = fixedFunctionShading;
        Properties = properties.ToArray();
    }
}
