using System.Reflection;
using Veldrid;

namespace Panopticon.Rendering;

public abstract class ShaderSet : DisposableBase
{
    private ushort _nextMaterialId;

    public readonly ushort Id;
    public readonly ShaderSetDescription Description;
    public readonly ResourceLayout[] ResourceLayouts;
    public readonly ResourceLayoutDescription[] ResourceLayoutDescriptions;

    public GraphicsDevice GraphicsDevice => Store.GraphicsDevice;

    protected readonly ShaderSetStore Store;

    protected ResourceLayout MaterialResourceLayout => ResourceLayouts[2];

    public ShaderSet(
        ShaderSetStore store,
        Assembly shaderAssembly,
        string shaderName,
        params VertexLayoutDescription[] vertexDescriptors)
        : this(store, shaderAssembly, shaderName, shaderName, vertexDescriptors)
    {
    }

    public ShaderSet(
        ShaderSetStore store,
        Assembly shaderAssembly,
        string vertexShaderName,
        string fragmentShaderName,
        params VertexLayoutDescription[] vertexDescriptors)
    {
        Store = store;

        Id = store.GetNextId();

        var factory = store.GraphicsDevice.ResourceFactory;

        var cacheFile = ShaderCrossCompiler.GetOrCreateCachedShaders(factory, shaderAssembly, vertexShaderName, fragmentShaderName);

        var vertexShader = AddDisposable(factory.CreateShader(cacheFile.VertexShaderDescription));
        vertexShader.Name = $"{vertexShaderName}.vert";

        var fragmentShader = AddDisposable(factory.CreateShader(cacheFile.FragmentShaderDescription));
        fragmentShader.Name = $"{fragmentShaderName}.frag";

        Description = new ShaderSetDescription(
            vertexDescriptors,
            new[] { vertexShader, fragmentShader });

        ResourceLayouts = new ResourceLayout[cacheFile.ResourceLayoutDescriptions.Length];
        ResourceLayoutDescriptions = cacheFile.ResourceLayoutDescriptions;
        for (var i = 0; i < cacheFile.ResourceLayoutDescriptions.Length; i++)
        {
            ResourceLayouts[i] = AddDisposable(
                factory.CreateResourceLayout(
                    ref cacheFile.ResourceLayoutDescriptions[i]));
        }
    }

    internal ushort GetNextMaterialId()
    {
        return checked(_nextMaterialId++);
    }
}
