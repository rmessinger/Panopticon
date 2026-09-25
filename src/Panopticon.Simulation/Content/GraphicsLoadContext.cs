using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Content;

internal sealed class GraphicsLoadContext
{
    public GraphicsDevice GraphicsDevice { get; }
    public StandardGraphicsResources StandardGraphicsResources { get; }
    public ShaderResourceManager ShaderResources { get; }
    public ShaderSetStore ShaderSetStore { get; }

    public GraphicsLoadContext(
        GraphicsDevice graphicsDevice,
        StandardGraphicsResources standardGraphicsResources,
        ShaderResourceManager shaderResources,
        ShaderSetStore shaderSetStore)
    {
        GraphicsDevice = graphicsDevice;
        StandardGraphicsResources = standardGraphicsResources;
        ShaderResources = shaderResources;
        ShaderSetStore = shaderSetStore;
    }
}
