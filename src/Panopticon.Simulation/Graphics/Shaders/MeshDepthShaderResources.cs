using Panopticon.Graphics.Rendering.Shadows;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class MeshDepthShaderResources : ShaderSetBase
{
    public readonly Material Material;

    // Empty resource sets for pipeline slots that are declared in the shader but unused
    // during the depth/shadow pass. Metal requires all declared slots to be bound before drawing.
    public readonly ResourceSet[] EmptyResourceSets;

    public MeshDepthShaderResources(
        ShaderSetStore store)
        : base(store, "MeshDepth", MeshShaderResources.MeshVertex.VertexDescriptors)
    {
        var depthRasterizerState = RasterizerStateDescriptionUtility.DefaultFrontIsCounterClockwise;
        depthRasterizerState.DepthClipEnabled = false;
        depthRasterizerState.ScissorTestEnabled = false;

        var pipeline = AddDisposable(
            GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescription.SingleDisabled,
                    DepthStencilStateDescription.DepthOnlyLessEqual,
                    depthRasterizerState,
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    ShadowData.DepthPassDescription)));

        // B5-2: instanced shadow draws pair the depth pipeline with the instance stream;
        // every shadow bucket item on an instancing-eligible mesh draws through it.
        var instancedSet = AddDisposable(new StandaloneShaderSet(
            store,
            "MeshDepthInstanced",
            MeshShaderResources.MeshVertex.VertexDescriptors));

        var instancedPipeline = AddDisposable(
            GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescription.SingleDisabled,
                    DepthStencilStateDescription.DepthOnlyLessEqual,
                    depthRasterizerState,
                    PrimitiveTopology.TriangleList,
                    instancedSet.Description,
                    instancedSet.ResourceLayouts,
                    ShadowData.DepthPassDescription)));

        // B5-2: shadow items resolve their instanced variant through this same material.
        Material = AddDisposable(
            new Material(
                this,
                pipeline,
                null,
                null,
                SurfaceType.Opaque,
                instancedPipeline,
                instancedSet.ResourceLayouts[3]));

        // Create empty resource sets for any empty resource layouts (slots with 0 elements).
        // These are needed on Metal which requires all declared slots to be bound.
        EmptyResourceSets = new ResourceSet[ResourceLayouts.Length];
        for (var i = 0; i < ResourceLayouts.Length; i++)
        {
            if (ResourceLayoutDescriptions[i].Elements.Length == 0)
            {
                EmptyResourceSets[i] = AddDisposable(
                    GraphicsDevice.ResourceFactory.CreateResourceSet(
                        new ResourceSetDescription(ResourceLayouts[i])));
            }
        }
    }
}
