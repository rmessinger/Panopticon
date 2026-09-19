using System.Collections.Generic;
using System.Linq;
using OpenSage.Graphics;
using OpenSage.Rendering;
using Veldrid;

namespace OpenSage.Graphics.Shaders;

/// <summary>
/// Core metallic-roughness shader family. Owns one shader set, the shared mesh vertex layout
/// (static and skinned meshes select skinning through mesh constants), and the complete PBR
/// material resource layout: one constants buffer covering every deep-dive factor and UV
/// selector, five role textures, and five per-role samplers. Material and pipeline caching
/// arrive with WI-C3-08; metallic-roughness lighting with WI-C3-07. Deliberately does not
/// derive from the W3D-oriented <see cref="ShaderMaterialShaderResources"/>.
/// </summary>
internal sealed class PbrMetallicRoughnessShaderResources : ShaderSetBase
{
    public const int MaterialConstantsBufferSize = 96;

    public const int FlagHasBaseColorTexture = 0x01;
    public const int FlagHasMetallicRoughnessTexture = 0x02;
    public const int FlagHasNormalTexture = 0x04;
    public const int FlagHasOcclusionTexture = 0x08;
    public const int FlagHasEmissiveTexture = 0x10;
    public const int FlagDoubleSided = 0x20;

    public const int AlphaModeOpaque = 0;
    public const int AlphaModeMask = 1;
    public const int AlphaModeBlend = 2;

    public readonly Dictionary<string, ResourceBinding> MaterialResourceBindings;

    public PbrMetallicRoughnessShaderResources(ShaderSetStore store)
        : base(store, "PbrMetallicRoughness", MeshShaderResources.MeshVertex.VertexDescriptors)
    {
        var materialResourceBindings = CreateMaterialResourceBindings().ToArray();
        MaterialResourceBindings = materialResourceBindings.ToDictionary(x => x.Description.Name);
    }

    private static IEnumerable<ResourceBinding> CreateMaterialResourceBindings()
    {
        yield return new ResourceBinding(
            0,
            new ResourceLayoutElementDescription("MaterialConstants", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceType(
                "MaterialConstants",
                MaterialConstantsBufferSize,
                new ResourceTypeMember("BaseColorFactor", ResourceType.Vec4, 0),
                new ResourceTypeMember("EmissiveFactor", ResourceType.Vec4, 16),
                new ResourceTypeMember("MetallicFactor", ResourceType.Float, 32),
                new ResourceTypeMember("RoughnessFactor", ResourceType.Float, 36),
                new ResourceTypeMember("NormalScale", ResourceType.Float, 40),
                new ResourceTypeMember("OcclusionStrength", ResourceType.Float, 44),
                new ResourceTypeMember("AlphaCutoff", ResourceType.Float, 48),
                new ResourceTypeMember("AlphaMode", ResourceType.Int, 52),
                new ResourceTypeMember("Flags", ResourceType.Int, 56),
                new ResourceTypeMember("BaseColorUvSet", ResourceType.Int, 60),
                new ResourceTypeMember("MetallicRoughnessUvSet", ResourceType.Int, 64),
                new ResourceTypeMember("NormalUvSet", ResourceType.Int, 68),
                new ResourceTypeMember("OcclusionUvSet", ResourceType.Int, 72),
                new ResourceTypeMember("EmissiveUvSet", ResourceType.Int, 76),
                new ResourceTypeMember("_Padding", ResourceType.Int, 80)));

        yield return new ResourceBinding(
            1,
            new ResourceLayoutElementDescription("BaseColorTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceType.Texture2D);

        yield return new ResourceBinding(
            2,
            new ResourceLayoutElementDescription("MetallicRoughnessTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceType.Texture2D);

        yield return new ResourceBinding(
            3,
            new ResourceLayoutElementDescription("NormalTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceType.Texture2D);

        yield return new ResourceBinding(
            4,
            new ResourceLayoutElementDescription("OcclusionTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceType.Texture2D);

        yield return new ResourceBinding(
            5,
            new ResourceLayoutElementDescription("EmissiveTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceType.Texture2D);

        yield return new ResourceBinding(
            6,
            new ResourceLayoutElementDescription("BaseColorSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceType.Sampler);

        yield return new ResourceBinding(
            7,
            new ResourceLayoutElementDescription("MetallicRoughnessSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceType.Sampler);

        yield return new ResourceBinding(
            8,
            new ResourceLayoutElementDescription("NormalSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceType.Sampler);

        yield return new ResourceBinding(
            9,
            new ResourceLayoutElementDescription("OcclusionSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceType.Sampler);

        yield return new ResourceBinding(
            10,
            new ResourceLayoutElementDescription("EmissiveSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceType.Sampler);
    }
}
