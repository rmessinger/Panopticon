using System.Collections.Generic;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal static class ProjectileStreamShaderSetExtensions
{
    public static ProjectileStreamShaderResources GetProjectileStreamShaderSet(this ShaderSetStore store)
    {
        return store.GetShaderSet(() => new ProjectileStreamShaderResources(store));
    }
}

internal sealed class ProjectileStreamShaderResources : ShaderSetBase
{
    public readonly Pipeline Pipeline;

    private readonly Dictionary<Texture, Material> _cachedMaterials = new();

    public ProjectileStreamShaderResources(ShaderSetStore store)
        : base(store, "ProjectileStream", ProjectileStreamVertex.VertexDescriptor)
    {
        Pipeline = AddDisposable(
            store.GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescriptionUtility.SingleAdditiveBlendNoAlpha,
                    DepthStencilStateDescription.DepthOnlyLessEqualRead,
                    RasterizerStateDescriptionUtility.CullNoneSolid,
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    store.OutputDescription)));
    }

    public Material GetMaterial(Texture texture)
    {
        if (!_cachedMaterials.TryGetValue(texture, out var result))
        {
            var materialResourceSet = AddDisposable(
                GraphicsDevice.ResourceFactory.CreateResourceSet(
                    new ResourceSetDescription(
                        MaterialResourceLayout,
                        texture,
                        GraphicsDevice.LinearSampler)));

            result = AddDisposable(
                new Material(
                    this,
                    Pipeline,
                    materialResourceSet,
                    SurfaceType.Transparent));

            _cachedMaterials.Add(texture, result);
        }

        return result;
    }

    public struct ProjectileStreamVertex
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Direction;
        public float Side;
        public float V;
        public System.Numerics.Vector4 Color;

        public static readonly VertexLayoutDescription VertexDescriptor = new VertexLayoutDescription(
            new VertexElementDescription("POSITION", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("TEXCOORD", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("COLOR", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));
    }
}
