using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class GraphicsSettingsTests : MockedGameTest
{
    [Fact]
    public void LegacyPresetMatchesAuditedHardcodedValues()
    {
        var settings = GraphicsSettings.Legacy();

        Assert.Equal(1, settings.SampleCount);
        Assert.Equal(AlphaTestMode.LegacyCutoff, settings.AlphaTestMode);
        Assert.Equal(ColourSpaceMode.LegacyGamma, settings.ColourSpaceMode);
        Assert.Equal(4, settings.Anisotropy);
        Assert.Equal(0f, settings.MipLodBias);
        Assert.False(settings.PrefilteredTerrainTextures);
        Assert.Equal(ShadowTier.Original, settings.ShadowTier);
        Assert.Equal(UiMode.LegacyArt, settings.UiMode);
        Assert.False(settings.RealisticProportions);
        Assert.Equal(500, settings.TerrainDecalLimit);
        Assert.Equal(TerrainDecalLighting.LegacyTint, settings.TerrainDecalLighting);
    }

    [Fact]
    public void EnhancedPresetUsesClarityDefaults()
    {
        var legacy = GraphicsSettings.Legacy();
        var enhanced = GraphicsSettings.Enhanced();

        Assert.Equal(4, enhanced.SampleCount);
        Assert.Equal(AlphaTestMode.CoveragePreserving, enhanced.AlphaTestMode);
        Assert.Equal(ColourSpaceMode.Linear, enhanced.ColourSpaceMode);
        Assert.Equal(ColourSpaceMode.LegacyGamma, legacy.ColourSpaceMode);
        Assert.Equal(16, enhanced.Anisotropy);
        Assert.Equal(4, legacy.Anisotropy);
        Assert.Equal(legacy.MipLodBias, enhanced.MipLodBias);
        Assert.True(enhanced.PrefilteredTerrainTextures);
        Assert.Equal(ShadowTier.High, enhanced.ShadowTier);
        Assert.Equal(UiMode.DynamicHud, enhanced.UiMode);
        Assert.Equal(UiMode.LegacyArt, legacy.UiMode);
        Assert.Equal(legacy.RealisticProportions, enhanced.RealisticProportions);
        Assert.Equal(2000, enhanced.TerrainDecalLimit);
        Assert.Equal(TerrainDecalLighting.TerrainLit, enhanced.TerrainDecalLighting);
    }

    [Fact]
    public void WithRealisticProportionsCopiesEveryMemberAndFlipsOnlyTheFlag()
    {
        var enhanced = GraphicsSettings.Enhanced();
        var enabled = enhanced.WithRealisticProportions(true);

        Assert.True(enabled.RealisticProportions);
        Assert.False(enhanced.RealisticProportions);
        Assert.Equal(enhanced.SampleCount, enabled.SampleCount);
        Assert.Equal(enhanced.AlphaTestMode, enabled.AlphaTestMode);
        Assert.Equal(enhanced.ColourSpaceMode, enabled.ColourSpaceMode);
        Assert.Equal(enhanced.Anisotropy, enabled.Anisotropy);
        Assert.Equal(enhanced.MipLodBias, enabled.MipLodBias);
        Assert.Equal(enhanced.PrefilteredTerrainTextures, enabled.PrefilteredTerrainTextures);
        Assert.Equal(enhanced.ShadowTier, enabled.ShadowTier);
        Assert.Equal(enhanced.UiMode, enabled.UiMode);
        Assert.Equal(enhanced.TerrainDecalLimit, enabled.TerrainDecalLimit);
        Assert.Equal(enhanced.TerrainDecalLighting, enabled.TerrainDecalLighting);
    }

    [Fact]
    public void DecalOverridesSurviveOtherSettingsCopies()
    {
        var settings = GraphicsSettings.Enhanced().WithTerrainDecals(123, TerrainDecalLighting.LegacyTint)
            .WithPrefilteredTerrainTextures(false).WithRealisticProportions(true);
        Assert.Equal(123, settings.TerrainDecalLimit);
        Assert.Equal(TerrainDecalLighting.LegacyTint, settings.TerrainDecalLighting);
    }

    [Fact]
    public void PresetFactoriesReturnDistinctInstances()
    {
        Assert.NotSame(GraphicsSettings.Legacy(), GraphicsSettings.Legacy());
        Assert.NotSame(GraphicsSettings.Legacy(), GraphicsSettings.Enhanced());
    }

    [Fact]
    public void TestGameHostDefaultsToLegacyPreset()
    {
        // The test host pins the 1x Legacy baseline; the Game default is Enhanced
        // (4x MSAA plus the high shadow tier).
        Assert.Equal(ShadowTier.Original, Generals.GraphicsSettings.ShadowTier);
    }

    [Fact]
    public void SwitchingPresetChangesActiveInstance()
    {
        var previous = Generals.GraphicsSettings;

        Generals.GraphicsSettings = GraphicsSettings.Enhanced();

        Assert.NotSame(previous, Generals.GraphicsSettings);
        Assert.Equal(ShadowTier.High, Generals.GraphicsSettings.ShadowTier);
    }
}
