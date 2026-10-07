using System.Numerics;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class WaterShaderResources : ShaderSetBase
{
    public readonly ResourceLayout WaterResourceLayout;

    public readonly Pipeline Pipeline;

    public WaterShaderResources(ShaderSetStore store)
        : base(store, "Water", WaterVertex.VertexDescriptor)
    {
        WaterResourceLayout = ResourceLayouts[2];

        Pipeline = AddDisposable(store.GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.DepthOnlyLessEqualRead,
                RasterizerStateDescriptionUtility.DefaultFrontIsCounterClockwise,
                PrimitiveTopology.TriangleList,
                Description,
                ResourceLayouts,
                store.OutputDescription)));
    }

    public struct WaterVertex
    {
        public Vector3 Position;

        public static readonly VertexLayoutDescription VertexDescriptor = new VertexLayoutDescription(
            new VertexElementDescription("POSITION", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));
    }
}
