using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class RoadShaderResources : ShaderSetBase
{
    public readonly Pipeline Pipeline;

    private readonly Dictionary<Texture, Material> _cachedMaterials = new();

    private Sampler _worldSampler;

    public RoadShaderResources(ShaderSetStore store, Sampler worldSampler)
        : base(store, "Road", RoadVertex.VertexDescriptor)
    {
        _worldSampler = worldSampler;

        Pipeline = AddDisposable(
            store.GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescription.SingleAlphaBlend,
                    DepthStencilStateDescription.DepthOnlyLessEqualRead,
                    RasterizerStateDescriptionUtility.CullNoneSolid, // TODO
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    store.OutputDescription)));
    }

    public Material GetMaterial(Texture texture)
    {
        if (!_cachedMaterials.TryGetValue(texture, out var result))
        {
            result = AddDisposable(
                new Material(
                    this,
                    Pipeline,
                    CreateMaterialResourceSet(texture),
                    SurfaceType.Transparent));

            _cachedMaterials.Add(texture, result);
        }

        return result;
    }

    /// <summary>Rebinds every cached road material to the new world sampler.</summary>
    internal void RebuildWorldSamplerSets(Sampler worldSampler)
    {
        _worldSampler = worldSampler;

        foreach (var texture in _cachedMaterials.Keys.ToArray())
        {
            var material = _cachedMaterials[texture];
            var oldSet = material.MaterialResourceSet;

            material.UpdateMaterialResourceSet(CreateMaterialResourceSet(texture));
            RemoveAndDispose(ref oldSet);
        }
    }

    private ResourceSet CreateMaterialResourceSet(Texture texture) => AddDisposable(
        GraphicsDevice.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(
                MaterialResourceLayout,
                texture,
                _worldSampler)));

    [StructLayout(LayoutKind.Sequential)]
    public struct RoadVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public static readonly VertexLayoutDescription VertexDescriptor = new VertexLayoutDescription(
            new VertexElementDescription("POSITION", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("NORMAL", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
    }
}
