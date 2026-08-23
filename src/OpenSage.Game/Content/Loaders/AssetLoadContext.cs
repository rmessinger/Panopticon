#nullable enable annotations
using OpenSage.Graphics;
using OpenSage.Graphics.Shaders;
using OpenSage.IO;
using OpenSage.Rendering;
using Veldrid;

namespace OpenSage.Content.Loaders;

internal sealed class AssetLoadContext
{
    public FileSystem FileSystem { get; }
    public string Language { get; }

    /// <summary>
    /// Null under data-only loading (A1-2); only the on-demand loaders and render-resource
    /// construction read the graphics fields, and both are absent in that mode.
    /// </summary>
    public GraphicsDevice? GraphicsDevice { get; }
    public StandardGraphicsResources? StandardGraphicsResources { get; }
    public ShaderResourceManager? ShaderResources { get; }
    public ShaderSetStore? ShaderSetStore { get; }
    public AssetStore AssetStore { get; }

    public AssetLoadContext(
        FileSystem fileSystem,
        string? language,
        GraphicsDevice? graphicsDevice,
        StandardGraphicsResources? standardGraphicsResources,
        ShaderResourceManager? shaderResources,
        ShaderSetStore? shaderSetStore,
        AssetStore assetStore)
    {
        FileSystem = fileSystem;
        Language = language;
        GraphicsDevice = graphicsDevice;
        StandardGraphicsResources = standardGraphicsResources;
        ShaderResources = shaderResources;
        ShaderSetStore = shaderSetStore;
        AssetStore = assetStore;
    }
}
