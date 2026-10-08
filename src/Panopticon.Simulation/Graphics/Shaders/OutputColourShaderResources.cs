using System.Collections.Generic;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

/// <summary>
/// Fullscreen pass that encodes the linear intermediate into the sRGB output, with a
/// zero-mean ordered dither before 8-bit quantization. Built only under the Linear colour
/// space mode (G6); the Legacy path keeps <see cref="TextureCopier"/>.
/// </summary>
internal sealed class OutputColourShaderResources : ShaderSetBase
{
    private readonly Dictionary<Texture, ResourceSet> _sourceResourceSets;
    private readonly Pipeline _pipeline;

    public OutputColourShaderResources(ShaderSetStore store, in OutputDescription outputDescription)
        : base(store, "OutputColour")
    {
        _sourceResourceSets = new Dictionary<Texture, ResourceSet>();

        _pipeline = AddDisposable(GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                BlendStateDescription.SingleDisabled,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescriptionUtility.CullNoneSolid,
                PrimitiveTopology.TriangleList,
                Description,
                ResourceLayouts,
                outputDescription)));
    }

    public Pipeline Pipeline => _pipeline;

    public ResourceSet GetOrCreateSourceResourceSet(Texture source, Sampler sampler)
    {
        if (!_sourceResourceSets.TryGetValue(source, out var resourceSet))
        {
            // The reflected layout declares SourceSampler in slot 0 and SourceTexture in slot 1.
            _sourceResourceSets.Add(source, resourceSet = AddDisposable(GraphicsDevice.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(ResourceLayouts[0], sampler, source))));
        }

        return resourceSet;
    }
}
