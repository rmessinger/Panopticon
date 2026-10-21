using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class TerrainTextureFilteringTests
{
    [Fact]
    public void EnhancedPrefersBakedData_LegacyKeepsOriginalMipPath()
    {
        Assert.False(GraphicsSettings.Legacy().PrefilteredTerrainTextures);
        Assert.True(GraphicsSettings.Enhanced().PrefilteredTerrainTextures);
    }

    [Fact]
    public void OverrideChangesOnlyTerrainFiltering_AndCopiesPreserveIt()
    {
        var original = GraphicsSettings.Enhanced();
        var modified = original.WithPrefilteredTerrainTextures(false);
        Assert.False(modified.PrefilteredTerrainTextures);
        Assert.Equal(original.SampleCount, modified.SampleCount);
        Assert.Equal(original.AlphaTestMode, modified.AlphaTestMode);
        Assert.Equal(original.ColourSpaceMode, modified.ColourSpaceMode);
        Assert.Equal(original.Anisotropy, modified.Anisotropy);
        Assert.Equal(original.MipLodBias, modified.MipLodBias);
        Assert.Equal(original.ShadowTier, modified.ShadowTier);
        Assert.Equal(original.UiMode, modified.UiMode);
        Assert.Equal(original.RealisticProportions, modified.RealisticProportions);
        Assert.False(modified.WithRealisticProportions(true).PrefilteredTerrainTextures);
        Assert.True(modified.WithPrefilteredTerrainTextures(true).PrefilteredTerrainTextures);
    }

    [GraphicsFact]
    public void CapabilityValidationPreservesDataSelection()
    {
        using var device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var controller = new GraphicsSettingsController(device);
        controller.Apply(GraphicsSettings.Enhanced());
        Assert.True(controller.Effective.PrefilteredTerrainTextures);
        controller.Apply(GraphicsSettings.Legacy());
        Assert.False(controller.Effective.PrefilteredTerrainTextures);
    }

    [GraphicsFact]
    public void SessionPinReportsRestartInsteadOfPretendingToChangeLoadedData()
    {
        using var device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var controller = new GraphicsSettingsController(device);
        controller.Apply(GraphicsSettings.Enhanced(), sessionPrefilteredTerrainTextures: false);
        Assert.False(controller.Effective.PrefilteredTerrainTextures);
        Assert.Contains(controller.Fallbacks, f => f.Contains("restart"));
    }
}
