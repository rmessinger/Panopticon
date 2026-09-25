using Panopticon.Graphics.Rendering;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal static class TracerShaderSetExtensions
{
    public static TracerShaderResources GetTracerShaderSet(this ShaderSetStore store)
    {
        return store.GetShaderSet(() => new TracerShaderResources(store));
    }
}

internal sealed class TracerShaderResources : ShaderSetBase
{
    public readonly Material Material;

    public TracerShaderResources(ShaderSetStore store)
        : base(store, "Tracer", TracerVertex.VertexDescriptor)
    {
        var pipeline = AddDisposable(
            store.GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescriptionUtility.SingleAdditiveBlendNoAlpha,
                    DepthStencilStateDescription.DepthOnlyLessEqualRead,
                    RasterizerStateDescriptionUtility.DefaultFrontIsCounterClockwise,
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    RenderPipeline.GameOutputDescription)));

        Material = AddDisposable(
            new Material(
                this,
                pipeline,
                null,
                SurfaceType.Transparent));
    }

    internal struct TracerVertex
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Direction;
        public System.Numerics.Vector4 Color;
        public float Length;
        public float Width;

        public static readonly VertexLayoutDescription VertexDescriptor = new VertexLayoutDescription(
            new VertexElementDescription("POSITION", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("COLOR", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));
    }
}
