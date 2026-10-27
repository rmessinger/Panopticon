using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class TerrainDecalShaderResources : ShaderSetBase
{
    public readonly Pipeline Pipeline;

    public TerrainDecalShaderResources(ShaderSetStore store)
        : base(store, "Road", "TerrainDecal", RoadShaderResources.RoadVertex.VertexDescriptor)
    {
        Pipeline = AddDisposable(store.GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.DepthOnlyLessEqualRead,
                RasterizerStateDescriptionUtility.CullNoneSolid,
                PrimitiveTopology.TriangleList,
                Description,
                ResourceLayouts,
                store.OutputDescription)));
    }

    internal ResourceSet CreateMaterialResourceSet(Texture texture, Sampler sampler) =>
        GraphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(MaterialResourceLayout, texture, sampler));
}
