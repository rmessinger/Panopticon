using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal abstract class ShaderSetBase : ShaderSet
{
    public ShaderSetBase(
        ShaderSetStore store,
        string shaderName,
        params VertexLayoutDescription[] vertexDescriptors)
        : base(store, typeof(ShaderSetBase).Assembly, shaderName, vertexDescriptors)
    {

    }

    public ShaderSetBase(
        ShaderSetStore store,
        string vertexShaderName,
        string fragmentShaderName,
        params VertexLayoutDescription[] vertexDescriptors)
        : base(store, typeof(ShaderSetBase).Assembly, vertexShaderName, fragmentShaderName, vertexDescriptors)
    {
    }
}
