using System;
using System.Collections.Generic;
using Panopticon.Utilities;
using Panopticon.Utilities.Extensions;
using Veldrid;

namespace Panopticon.Graphics;

internal sealed class StandardGraphicsResources : DisposableBase
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<uint, DeviceBuffer> _nullStructuredBuffers;

    public Sampler Aniso4xClampSampler { get; }
    public Sampler LinearClampSampler { get; }
    public Sampler PointClampSampler { get; }

    public Texture NullTexture { get; }
    public Texture SolidWhiteTexture { get; }
    public Texture SolidBlackTexture { get; }
    public Texture PlaceholderTexture { get; }

    /// <summary>
    /// Retained copy of the constructor settings: shader managers and material builders
    /// consult the alpha-test mode and other members when selecting variants (WI-G5-02).
    /// </summary>
    public GraphicsSettings Settings { get; }

    /// <summary>
    /// Wrap-mode anisotropic world sampler driven by the anisotropy and mip bias settings
    /// (WI-G7-02). Replaces <see cref="GraphicsDevice.Aniso4xSampler"/> at the world-material
    /// binding sites; recreated live through <see cref="RecreateWorldSampler"/>.
    /// </summary>
    public Sampler WorldAnisoSampler { get; private set; }

    private Sampler _retiredWorldAnisoSampler;
    private int _appliedWorldAnisotropy;
    private float _appliedWorldLodBias;

    public StandardGraphicsResources(GraphicsDevice graphicsDevice, GraphicsSettings settings)
    {
        _graphicsDevice = graphicsDevice;
        Settings = settings;

        WorldAnisoSampler = CreateWorldAnisoSampler(settings);
        _appliedWorldAnisotropy = settings.Anisotropy;
        _appliedWorldLodBias = settings.MipLodBias;

        var aniso4xClampSamplerDescription = SamplerDescription.Aniso4x;
        aniso4xClampSamplerDescription.AddressModeU = SamplerAddressMode.Clamp;
        aniso4xClampSamplerDescription.AddressModeV = SamplerAddressMode.Clamp;
        aniso4xClampSamplerDescription.AddressModeW = SamplerAddressMode.Clamp;
        aniso4xClampSamplerDescription.MaximumAnisotropy = (uint)settings.Anisotropy;

        // The vendored Veldrid stores the bias as an integer; G7 owns bounded sub-integer bias.
        aniso4xClampSamplerDescription.LodBias = (int)MathF.Round(settings.MipLodBias);
        Aniso4xClampSampler = AddDisposable(
            graphicsDevice.ResourceFactory.CreateSampler(ref aniso4xClampSamplerDescription));
        Aniso4xClampSampler.Name = "Aniso4xClamp Sampler";

        var linearClampSamplerDescription = SamplerDescription.Linear;
        linearClampSamplerDescription.AddressModeU = SamplerAddressMode.Clamp;
        linearClampSamplerDescription.AddressModeV = SamplerAddressMode.Clamp;
        linearClampSamplerDescription.AddressModeW = SamplerAddressMode.Clamp;
        LinearClampSampler = AddDisposable(
            graphicsDevice.ResourceFactory.CreateSampler(ref linearClampSamplerDescription));
        LinearClampSampler.Name = "LinearClamp Sampler";

        var pointClampSamplerDescription = SamplerDescription.Point;
        pointClampSamplerDescription.AddressModeU = SamplerAddressMode.Clamp;
        pointClampSamplerDescription.AddressModeV = SamplerAddressMode.Clamp;
        pointClampSamplerDescription.AddressModeW = SamplerAddressMode.Clamp;
        PointClampSampler = AddDisposable(
            graphicsDevice.ResourceFactory.CreateSampler(ref pointClampSamplerDescription));
        PointClampSampler.Name = "PointClamp Sampler";

        NullTexture = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)));
        NullTexture.Name = "Null Texture";

        SolidWhiteTexture = AddDisposable(graphicsDevice.CreateStaticTexture2D(
            1, 1, 1,
            new TextureMipMapData(
                new byte[] { 255, 255, 255, 255 },
                4, 4, 1, 1),
            PixelFormat.R8_G8_B8_A8_UNorm));
        SolidWhiteTexture.Name = "Solid White Texture";

        SolidBlackTexture = AddDisposable(graphicsDevice.CreateStaticTexture2D(
            1, 1, 1,
            new TextureMipMapData(
                new byte[] { 0, 0, 0, 255 },
                4, 4, 1, 1),
            PixelFormat.R8_G8_B8_A8_UNorm));
        SolidBlackTexture.Name = "Solid Black Texture";

        PlaceholderTexture = AddDisposable(graphicsDevice.CreateStaticTexture2D(
            1, 1, 1,
            new TextureMipMapData(
                new byte[] { 255, 105, 180, 255 },
                4, 4, 1, 1),
            PixelFormat.R8_G8_B8_A8_UNorm));
        PlaceholderTexture.Name = "Placeholder Texture";

        _nullStructuredBuffers = new Dictionary<uint, DeviceBuffer>();
    }

    /// <summary>
    /// True when the world sampler no longer reflects <paramref name="settings"/> and a
    /// live rebuild is due.
    /// </summary>
    internal bool WorldSamplerNeedsRebuild(GraphicsSettings settings)
    {
        return settings.Anisotropy != _appliedWorldAnisotropy
            || settings.MipLodBias != _appliedWorldLodBias;
    }

    /// <summary>
    /// Builds the replacement world sampler and retires the current one. The retired sampler
    /// stays alive until <see cref="DisposeRetiredWorldSampler"/> runs, after the resource sets
    /// that referenced it were rebuilt.
    /// </summary>
    internal Sampler RecreateWorldSampler(GraphicsSettings settings)
    {
        _retiredWorldAnisoSampler = WorldAnisoSampler;
        WorldAnisoSampler = CreateWorldAnisoSampler(settings);
        _appliedWorldAnisotropy = settings.Anisotropy;
        _appliedWorldLodBias = settings.MipLodBias;
        return WorldAnisoSampler;
    }

    internal void DisposeRetiredWorldSampler()
    {
        _retiredWorldAnisoSampler?.Dispose();
        _retiredWorldAnisoSampler = null;
    }

    private Sampler CreateWorldAnisoSampler(GraphicsSettings settings)
    {
        var description = SamplerDescription.Aniso4x;
        description.MaximumAnisotropy = (uint)GraphicsSettingsController.ClampAnisotropy(_graphicsDevice, settings.Anisotropy);
        description.LodBias = (int)MathF.Round(settings.MipLodBias);
        var sampler = _graphicsDevice.ResourceFactory.CreateSampler(ref description);
        sampler.Name = "WorldAniso Sampler";
        return sampler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Tracked manually: retired samplers outlive AddDisposable registration.
            WorldAnisoSampler.Dispose();
            _retiredWorldAnisoSampler?.Dispose();
            _retiredWorldAnisoSampler = null;
        }

        base.Dispose(disposing);
    }

    internal DeviceBuffer GetNullStructuredBuffer(uint size)
    {
        if (!_nullStructuredBuffers.TryGetValue(size, out var result))
        {
            _nullStructuredBuffers.Add(size, result = AddDisposable(_graphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    size,
                    BufferUsage.StructuredBufferReadOnly,
                    size,
                    true))));

            result.Name = $"NullStructuredBuffer_Size{size}";
        }

        return result;
    }
}
