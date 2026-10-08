using System;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Rendering.Shadows;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class GraphicsSettingsApplicationTests
{
    [Fact]
    public void TestGameOutputDescriptionIsEnhancedDescription()
    {
        var expected = RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Enhanced());

        Assert.Equal(expected.DepthAttachment.Value.Format, RenderPipeline.GameOutputDescription.DepthAttachment.Value.Format);
        Assert.Equal(expected.ColorAttachments[0].Format, RenderPipeline.GameOutputDescription.ColorAttachments[0].Format);
        Assert.Equal(expected.SampleCount, RenderPipeline.GameOutputDescription.SampleCount);
        Assert.Equal(TextureSampleCount.Count4, RenderPipeline.GameOutputDescription.SampleCount);

        // G6: the linear preset renders into a float intermediate; Legacy keeps 8-bit UNorm.
        Assert.Equal(PixelFormat.R16_G16_B16_A16_Float, RenderPipeline.GameOutputDescription.ColorAttachments[0].Format);
        Assert.Equal(
            PixelFormat.B8_G8_R8_A8_UNorm,
            RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Legacy()).ColorAttachments[0].Format);
    }

    [Fact]
    public void TestEnhancedPresetSelectsFourSamples()
    {
        var description = RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Enhanced());

        Assert.Equal(TextureSampleCount.Count4, description.SampleCount);
        Assert.Equal(RenderPipeline.GameOutputDescription.DepthAttachment.Value.Format, description.DepthAttachment.Value.Format);
        Assert.Equal(RenderPipeline.GameOutputDescription.ColorAttachments[0].Format, description.ColorAttachments[0].Format);
    }

    [Fact]
    public void TestExplicitSampleCountOverridesSettings()
    {
        // The 2D/UI path shares the settings formats but stays single-sample under MSAA.
        var description = RenderPipeline.CreateGameOutputDescription(
            GraphicsSettings.Enhanced(),
            TextureSampleCount.Count1);

        Assert.Equal(TextureSampleCount.Count1, description.SampleCount);
    }

    [Fact]
    public void TestSampleCountMapsToTextureSampleCount()
    {
        var settings = GraphicsSettings.Create(
            sampleCount: 4,
            AlphaTestMode.LegacyCutoff,
            ColourSpaceMode.LegacyGamma,
            anisotropy: 4,
            mipLodBias: 0f,
            ShadowTier.Original,
            UiMode.LegacyArt,
            realisticProportions: false);

        var description = RenderPipeline.CreateGameOutputDescription(settings);

        Assert.Equal(TextureSampleCount.Count4, description.SampleCount);
        Assert.Equal(RenderPipeline.GameOutputDescription.DepthAttachment.Value.Format, description.DepthAttachment.Value.Format);
        Assert.Equal(PixelFormat.B8_G8_R8_A8_UNorm, description.ColorAttachments[0].Format);
    }

    [Fact]
    public void TestApplyTierOriginalKeepsDefaults()
    {
        var settings = new ShadowSettings();

        settings.ApplyTier(ShadowTier.Original);

        Assert.Equal(ShadowMapCascades.OneCascade, settings.ShadowMapCascades);
        Assert.Equal(1024u, settings.ShadowMapSize);
        Assert.Equal(0.002f, settings.Bias);
        Assert.Equal(0.4f, settings.NormalOffset);
    }

    [Fact]
    public void TestApplyTierHighSetsCascadesAndSizeOnly()
    {
        var settings = new ShadowSettings();

        settings.ApplyTier(ShadowTier.High);

        Assert.Equal(ShadowMapCascades.FourCascades, settings.ShadowMapCascades);
        Assert.Equal(2048u, settings.ShadowMapSize);
        Assert.Equal(ShadowsType.Soft, settings.ShadowsType);
        Assert.Equal(0.002f, settings.Bias);
        Assert.Equal(0.4f, settings.NormalOffset);

        settings.ApplyTier(ShadowTier.Original);

        Assert.Equal(ShadowMapCascades.OneCascade, settings.ShadowMapCascades);
        Assert.Equal(1024u, settings.ShadowMapSize);
    }

    [GraphicsFact]
    public void TestControllerClampsSampleCountToDeviceLimit()
    {
        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var controller = new GraphicsSettingsController(graphicsDevice);

        var requested = GraphicsSettings.Create(
            sampleCount: 64,
            AlphaTestMode.LegacyCutoff,
            ColourSpaceMode.LegacyGamma,
            anisotropy: 4,
            mipLodBias: 0f,
            ShadowTier.High,
            UiMode.LegacyArt,
            realisticProportions: false);
        controller.Apply(requested);

        var limit = GraphicsSettingsController.FromTextureSampleCount(
            graphicsDevice.GetSampleCountLimit(PixelFormat.D32_Float_S8_UInt, depthFormat: true));

        Assert.True(controller.Effective.SampleCount <= limit, $"Effective {controller.Effective.SampleCount}, limit {limit}.");
        Assert.NotEqual(requested.SampleCount, controller.Effective.SampleCount);
        Assert.NotEmpty(controller.Fallbacks);
    }

    [GraphicsFact]
    public void TestControllerSessionClampReportsPinnedSampleCount()
    {
        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var controller = new GraphicsSettingsController(graphicsDevice);

        // A session constructed at 1x must never report a higher effective count, even when
        // the device and the preset both support 4x.
        controller.Apply(GraphicsSettings.Enhanced(), sessionSampleCount: 1);

        Assert.Equal(1, controller.Effective.SampleCount);
        Assert.Contains(controller.Fallbacks, f => f.Contains("session"));
    }

    [GraphicsFact]
    public void TestControllerRoundTripRestoresLegacyValues()
    {
        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var controller = new GraphicsSettingsController(graphicsDevice);

        controller.Apply(GraphicsSettings.Enhanced());
        var enhanced = controller.Effective;

        controller.Apply(GraphicsSettings.Legacy());
        var legacy = controller.Effective;

        Assert.Equal(1, legacy.SampleCount);
        Assert.Equal(ShadowTier.Original, legacy.ShadowTier);
        Assert.Equal(enhanced.Anisotropy, legacy.Anisotropy);
        Assert.Equal(enhanced.MipLodBias, legacy.MipLodBias);
    }
}
