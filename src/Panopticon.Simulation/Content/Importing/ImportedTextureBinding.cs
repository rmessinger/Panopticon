#nullable enable

using System;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedTextureRole
{
    /// <summary>sRGB RGBA base color.</summary>
    BaseColor,

    /// <summary>Linear; roughness in G, metallic in B.</summary>
    MetallicRoughness,

    /// <summary>Linear tangent-space RGB normal map.</summary>
    Normal,

    /// <summary>Linear occlusion in R.</summary>
    Occlusion,

    /// <summary>sRGB RGB emissive.</summary>
    Emissive,
}

/// <summary>
/// One texture binding: immutable image source, UV set (0 or 1), sampler, and role. The role
/// determines channel use and color space at materialization time.
/// </summary>
public sealed class ImportedTextureBinding
{
    public ImportedImageSource ImageSource { get; }

    public int UvSet { get; }

    public ImportedSampler Sampler { get; }

    public ImportedTextureRole Role { get; }

    public ImportedTextureBinding(
        ImportedImageSource imageSource,
        int uvSet,
        ImportedSampler sampler,
        ImportedTextureRole role)
    {
        ArgumentNullException.ThrowIfNull(imageSource);

        ArgumentNullException.ThrowIfNull(sampler);

        Ensure.InRange(uvSet, 0, 1, "UV set must be 0 or 1.");

        ImageSource = imageSource;
        UvSet = uvSet;
        Sampler = sampler;
        Role = role;
    }
}
