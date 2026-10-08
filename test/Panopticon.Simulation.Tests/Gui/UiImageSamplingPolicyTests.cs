using System;
using Panopticon.Graphics;
using Panopticon.Gui;
using Xunit;

namespace Panopticon.Tests.Gui;

[Collection("GraphicsTests")]
public class UiImageSamplingPolicyTests
{
    [GraphicsFact]
    public void DynamicHudModeSelectsTheAnisotropicClampSampler()
    {
        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        using var resources = new StandardGraphicsResources(graphicsDevice, GraphicsSettings.Enhanced());

        Assert.Same(resources.Aniso4xClampSampler, UiImageSamplingPolicy.SelectMappedImageSampler(resources));
    }

    [GraphicsFact]
    public void LegacyUiModeKeepsThePassSampler()
    {
        using var graphicsDevice = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        using var resources = new StandardGraphicsResources(graphicsDevice, GraphicsSettings.Legacy());

        Assert.Null(UiImageSamplingPolicy.SelectMappedImageSampler(resources));
    }
}
