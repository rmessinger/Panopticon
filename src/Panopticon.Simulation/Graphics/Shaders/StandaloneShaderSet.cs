using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

/// <summary>
/// Concrete, unadorned shader set used for secondary shader sets a family owns beside its
/// main one (B5-2: the instanced variants). Adds nothing over <see cref="ShaderSetBase"/>.
/// </summary>
internal sealed class StandaloneShaderSet : ShaderSetBase
{
    public StandaloneShaderSet(
        ShaderSetStore store,
        string shaderName,
        params VertexLayoutDescription[] vertexDescriptors)
        : base(store, shaderName, vertexDescriptors)
    {
    }

    public StandaloneShaderSet(
        ShaderSetStore store,
        string vertexShaderName,
        string fragmentShaderName,
        params VertexLayoutDescription[] vertexDescriptors)
        : base(store, vertexShaderName, fragmentShaderName, vertexDescriptors)
    {
    }
}
