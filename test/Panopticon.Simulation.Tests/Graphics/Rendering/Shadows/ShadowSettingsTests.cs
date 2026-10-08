using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering.Shadows;
using Panopticon.Graphics.Shaders;
using Xunit;

namespace Panopticon.Simulation.Tests.Graphics.Rendering.Shadows;

public class ShadowSettingsTests
{
    [Fact]
    public void TestShadowConstantsPsLayoutStaysAt240Bytes()
    {
        Assert.Equal(240, Marshal.SizeOf<GlobalShaderResources.ShadowConstantsPS>());
    }

    [Fact]
    public void TestApplyTierOriginalLeavesDefaultSettingsUnchanged()
    {
        var settings = new ShadowSettings();
        var expected = new ShadowSettings();

        settings.ApplyTier(ShadowTier.Original);

        Assert.Equal(expected.ShadowMapCascades, settings.ShadowMapCascades);
        Assert.Equal(expected.ShadowsType, settings.ShadowsType);
        Assert.Equal(expected.ShadowMapSize, settings.ShadowMapSize);
        Assert.Equal(expected.ShadowCascades2SplitDepth, settings.ShadowCascades2SplitDepth);
        Assert.Equal(expected.ShadowCascades4SplitDepths, settings.ShadowCascades4SplitDepths);
        Assert.Equal(expected.ShadowDistance, settings.ShadowDistance);
        Assert.Equal(expected.StabilizeShadowCascades, settings.StabilizeShadowCascades);
        Assert.Equal(expected.VisualizeCascades, settings.VisualizeCascades);
        Assert.Equal(expected.UpdateShadowMatrices, settings.UpdateShadowMatrices);
        Assert.Equal(expected.VisualizeShadowFrustums, settings.VisualizeShadowFrustums);
        Assert.Equal(expected.Bias, settings.Bias);
        Assert.Equal(expected.SlopeScaledBias, settings.SlopeScaledBias);
        Assert.Equal(expected.NormalOffset, settings.NormalOffset);
    }

    [Fact]
    public void TestApplyTierHighSetsFourCascadesAndSlopeBias()
    {
        var settings = new ShadowSettings();

        settings.ApplyTier(ShadowTier.High);

        Assert.Equal(ShadowMapCascades.FourCascades, settings.ShadowMapCascades);
        Assert.Equal(2048u, settings.ShadowMapSize);
        Assert.Equal(2f, settings.SlopeScaledBias);
    }

    [Fact]
    public void TestApplyTierPreservesMembersItDoesNotOwn()
    {
        var settings = new ShadowSettings
        {
            Bias = 0.004f,
            NormalOffset = 0.7f,
            ShadowDistance = 800f,
            ShadowCascades4SplitDepths = new Vector3(0.1f, 0.3f, 0.6f),
        };

        settings.ApplyTier(ShadowTier.High);

        Assert.Equal(0.004f, settings.Bias);
        Assert.Equal(0.7f, settings.NormalOffset);
        Assert.Equal(800f, settings.ShadowDistance);
        Assert.Equal(new Vector3(0.1f, 0.3f, 0.6f), settings.ShadowCascades4SplitDepths);
    }

    [Fact]
    public void TestApplyTierRejectsUnknownTier()
    {
        var settings = new ShadowSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ApplyTier((ShadowTier)99));
    }
}
