using System;
using OpenSage.Content;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using OpenSage.IO;
using OpenSage.Mods.BuiltIn;
using OpenSage.Rendering;
using Veldrid;

namespace OpenSage.Tests.Content.Importing;

/// <summary>
/// Headless graphics device plus a fully wired asset store and load context for
/// GPU-backed materialization tests. The file system and load strategy are injectable so
/// installed-asset tests can resolve real textures through the legacy loaders.
/// </summary>
internal sealed class TestGraphicsContext : IDisposable
{
    public GraphicsDevice Device { get; }
    public StandardGraphicsResources StandardResources { get; }
    public ShaderSetStore ShaderSetStore { get; }
    public ShaderResourceManager ShaderResources { get; }
    public AssetLoadContext Context { get; }

    public TestGraphicsContext(
        FileSystem? fileSystem = null,
        OnDemandAssetLoadStrategy? loadStrategy = null)
    {
        Device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        StandardResources = new StandardGraphicsResources(Device);
        ShaderSetStore = new ShaderSetStore(Device, RenderPipeline.GameOutputDescription);
        ShaderResources = new ShaderResourceManager(Device, StandardResources, ShaderSetStore);

        fileSystem ??= new MemoryFileSystem();

        var assetStore = new AssetStore(
            SageGame.CncGenerals,
            fileSystem,
            "english",
            Device,
            StandardResources,
            ShaderResources,
            ShaderSetStore,
            loadStrategy ?? OnDemandAssetLoadStrategy.None);

        Context = new AssetLoadContext(
            fileSystem,
            "english",
            Device,
            StandardResources,
            ShaderResources,
            ShaderSetStore,
            assetStore);

        // On-demand asset resolution requires an active scope; the game pushes one at startup.
        assetStore.PushScope();
    }

    public void Dispose()
    {
        ShaderResources.Dispose();
        ShaderSetStore.Dispose();
        StandardResources.Dispose();
        Device.Dispose();
    }
}
