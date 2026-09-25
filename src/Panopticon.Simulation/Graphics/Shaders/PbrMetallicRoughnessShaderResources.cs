using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

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
    private readonly Dictionary<PipelineKey, Pipeline> _pipelineCache = new();
    private readonly Dictionary<PbrMaterialConstantsKey, DeviceBuffer> _materialConstantsBufferCache = new();
    private readonly Dictionary<MaterialResourceSetKey, ResourceSet> _materialResourceSetCache = new();
    private readonly Dictionary<MaterialKey, Material> _materialCache = new();

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

    /// <summary>
    /// Resolved GPU binding for one optional texture role: the uploaded texture (neutral
    /// engine-owned white texture only for omitted bindings) and its sampler. Texture identity is
    /// object identity: the C3-05 materializer already caches uploads by content.
    /// </summary>
    public readonly record struct RoleBinding(Texture Texture, Sampler Sampler);

    /// <summary>
    /// Creates or reuses a complete PBR material. Every output-affecting dimension is part of the
    /// cache key: all factors, alpha mode and cutoff, double-sidedness (pipeline: culling, depth
    /// write, blending, surface type), per-role texture and sampler identity, and UV selection.
    /// </summary>
    public Material GetCachedMaterial(
        ImportedPbrMetallicRoughness payload,
        StandardGraphicsResources standardGraphicsResources,
        RoleBinding? baseColor = null,
        RoleBinding? metallicRoughness = null,
        RoleBinding? normal = null,
        RoleBinding? occlusion = null,
        RoleBinding? emissive = null)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (standardGraphicsResources is null)
        {
            throw new ArgumentNullException(nameof(standardGraphicsResources));
        }

        var blendEnabled = payload.AlphaMode == ImportedAlphaMode.Blend;
        var pipelineKey = new PipelineKey(
            payload.DoubleSided ? FaceCullMode.None : FaceCullMode.Back,
            DepthWriteEnabled: !blendEnabled,
            BlendEnabled: blendEnabled,
            SourceFactor: blendEnabled ? BlendFactor.SourceAlpha : BlendFactor.One,
            DestinationColorFactor: blendEnabled ? BlendFactor.InverseSourceAlpha : BlendFactor.Zero,
            DestinationAlphaFactor: blendEnabled ? BlendFactor.InverseSourceAlpha : BlendFactor.Zero);

        var constantsKey = PbrMaterialConstantsKey.From(payload);

        var materialKey = new MaterialKey(
            pipelineKey,
            constantsKey,
            baseColor,
            metallicRoughness,
            normal,
            occlusion,
            emissive);

        if (!_materialCache.TryGetValue(materialKey, out var result))
        {
            var pipeline = GetCachedPipeline(pipelineKey);

            var materialResourceSet = GetCachedMaterialResourceSet(
                constantsKey,
                standardGraphicsResources,
                baseColor,
                metallicRoughness,
                normal,
                occlusion,
                emissive);

            result = AddDisposable(
                new Material(
                    this,
                    pipeline,
                    materialResourceSet,
                    blendEnabled ? SurfaceType.Transparent : SurfaceType.Opaque));

            _materialCache.Add(materialKey, result);
        }

        return result;
    }

    private Pipeline GetCachedPipeline(in PipelineKey pipelineKey)
    {
        if (!_pipelineCache.TryGetValue(pipelineKey, out var result))
        {
            var blendState = new BlendStateDescription(
                RgbaFloat.White,
                new BlendAttachmentDescription(
                    pipelineKey.BlendEnabled,
                    pipelineKey.SourceFactor,
                    pipelineKey.DestinationColorFactor,
                    BlendFunction.Add,
                    pipelineKey.SourceFactor,
                    pipelineKey.DestinationAlphaFactor,
                    BlendFunction.Add));

            var depthState = DepthStencilStateDescription.DepthOnlyLessEqual;
            depthState.DepthWriteEnabled = pipelineKey.DepthWriteEnabled;

            var rasterizerState = RasterizerStateDescriptionUtility.DefaultFrontIsCounterClockwise;
            rasterizerState.CullMode = pipelineKey.CullMode;

            _pipelineCache.Add(pipelineKey, result = AddDisposable(GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    blendState,
                    depthState,
                    rasterizerState,
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    Store.OutputDescription))));
        }

        return result;
    }

    private ResourceSet GetCachedMaterialResourceSet(
        in PbrMaterialConstantsKey constantsKey,
        StandardGraphicsResources standardGraphicsResources,
        RoleBinding? baseColor,
        RoleBinding? metallicRoughness,
        RoleBinding? normal,
        RoleBinding? occlusion,
        RoleBinding? emissive)
    {
        var resourceSetKey = new MaterialResourceSetKey(
            constantsKey,
            baseColor,
            metallicRoughness,
            normal,
            occlusion,
            emissive);

        if (!_materialResourceSetCache.TryGetValue(resourceSetKey, out var result))
        {
            var materialConstantsBuffer = GetCachedMaterialConstantsBuffer(constantsKey);

            // Neutral engine-owned textures and samplers stand in only for omitted bindings;
            // referenced images are resolved (and validated) by the caller through C3-05.
            var neutralTexture = standardGraphicsResources.SolidWhiteTexture;
            var neutralSampler = standardGraphicsResources.LinearClampSampler;

            var baseColorBinding = baseColor ?? new RoleBinding(neutralTexture, neutralSampler);
            var metallicRoughnessBinding = metallicRoughness ?? new RoleBinding(neutralTexture, neutralSampler);
            var normalBinding = normal ?? new RoleBinding(neutralTexture, neutralSampler);
            var occlusionBinding = occlusion ?? new RoleBinding(neutralTexture, neutralSampler);
            var emissiveBinding = emissive ?? new RoleBinding(neutralTexture, neutralSampler);

            result = AddDisposable(
                GraphicsDevice.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(
                        MaterialResourceLayout,
                        materialConstantsBuffer,
                        baseColorBinding.Texture,
                        metallicRoughnessBinding.Texture,
                        normalBinding.Texture,
                        occlusionBinding.Texture,
                        emissiveBinding.Texture,
                        baseColorBinding.Sampler,
                        metallicRoughnessBinding.Sampler,
                        normalBinding.Sampler,
                        occlusionBinding.Sampler,
                        emissiveBinding.Sampler)));

            _materialResourceSetCache.Add(resourceSetKey, result);
        }

        return result;
    }

    private DeviceBuffer GetCachedMaterialConstantsBuffer(in PbrMaterialConstantsKey key)
    {
        if (!_materialConstantsBufferCache.TryGetValue(key, out var result))
        {
            result = AddDisposable(GraphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(MaterialConstantsBufferSize, BufferUsage.UniformBuffer)));

            GraphicsDevice.UpdateBuffer(result, 0, key.Serialize());

            _materialConstantsBufferCache.Add(key, result);
        }

        return result;
    }

    private readonly record struct PipelineKey(
        FaceCullMode CullMode,
        bool DepthWriteEnabled,
        bool BlendEnabled,
        BlendFactor SourceFactor,
        BlendFactor DestinationColorFactor,
        BlendFactor DestinationAlphaFactor);

    private readonly record struct MaterialResourceSetKey(
        PbrMaterialConstantsKey Constants,
        RoleBinding? BaseColor,
        RoleBinding? MetallicRoughness,
        RoleBinding? Normal,
        RoleBinding? Occlusion,
        RoleBinding? Emissive);

    private readonly record struct MaterialKey(
        PipelineKey Pipeline,
        PbrMaterialConstantsKey Constants,
        RoleBinding? BaseColor,
        RoleBinding? MetallicRoughness,
        RoleBinding? Normal,
        RoleBinding? Occlusion,
        RoleBinding? Emissive);

    private readonly record struct PbrMaterialConstantsKey(
        Vector4 BaseColorFactor,
        Vector3 EmissiveFactor,
        float MetallicFactor,
        float RoughnessFactor,
        float NormalScale,
        float OcclusionStrength,
        float AlphaCutoff,
        int AlphaMode,
        int Flags,
        int BaseColorUvSet,
        int MetallicRoughnessUvSet,
        int NormalUvSet,
        int OcclusionUvSet,
        int EmissiveUvSet)
    {
        public static PbrMaterialConstantsKey From(ImportedPbrMetallicRoughness payload)
        {
            var flags = 0;
            flags |= payload.BaseColorTexture is not null ? FlagHasBaseColorTexture : 0;
            flags |= payload.MetallicRoughnessTexture is not null ? FlagHasMetallicRoughnessTexture : 0;
            flags |= payload.NormalTexture is not null ? FlagHasNormalTexture : 0;
            flags |= payload.OcclusionTexture is not null ? FlagHasOcclusionTexture : 0;
            flags |= payload.EmissiveTexture is not null ? FlagHasEmissiveTexture : 0;
            flags |= payload.DoubleSided ? FlagDoubleSided : 0;

            return new PbrMaterialConstantsKey(
                payload.BaseColorFactor,
                payload.EmissiveFactor,
                payload.MetallicFactor,
                payload.RoughnessFactor,
                payload.NormalScale,
                payload.OcclusionStrength,
                payload.AlphaCutoff,
                (int)payload.AlphaMode,
                flags,
                payload.BaseColorTexture?.UvSet ?? 0,
                payload.MetallicRoughnessTexture?.UvSet ?? 0,
                payload.NormalTexture?.UvSet ?? 0,
                payload.OcclusionTexture?.UvSet ?? 0,
                payload.EmissiveTexture?.UvSet ?? 0);
        }

        public byte[] Serialize()
        {
            var bytes = new byte[MaterialConstantsBufferSize];
            Write(bytes, 0, BaseColorFactor);
            Write(bytes, 16, EmissiveFactor);
            Write(bytes, 32, MetallicFactor);
            Write(bytes, 36, RoughnessFactor);
            Write(bytes, 40, NormalScale);
            Write(bytes, 44, OcclusionStrength);
            Write(bytes, 48, AlphaCutoff);
            Write(bytes, 52, AlphaMode);
            Write(bytes, 56, Flags);
            Write(bytes, 60, BaseColorUvSet);
            Write(bytes, 64, MetallicRoughnessUvSet);
            Write(bytes, 68, NormalUvSet);
            Write(bytes, 72, OcclusionUvSet);
            Write(bytes, 76, EmissiveUvSet);
            return bytes;
        }

        private static void Write(byte[] bytes, int offset, in Vector4 value)
        {
            Write(bytes, offset, value.X);
            Write(bytes, offset + 4, value.Y);
            Write(bytes, offset + 8, value.Z);
            Write(bytes, offset + 12, value.W);
        }

        private static void Write(byte[] bytes, int offset, in Vector3 value)
        {
            Write(bytes, offset, value.X);
            Write(bytes, offset + 4, value.Y);
            Write(bytes, offset + 8, value.Z);
        }

        private static void Write(byte[] bytes, int offset, float value)
        {
            BitConverter.GetBytes(value).CopyTo(bytes, offset);
        }

        private static void Write(byte[] bytes, int offset, int value)
        {
            BitConverter.GetBytes(value).CopyTo(bytes, offset);
        }
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
