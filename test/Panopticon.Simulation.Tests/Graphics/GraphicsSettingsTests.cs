using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class GraphicsSettingsTests : MockedGameTest
{
    [Fact]
    public void FaithfulPresetMatchesAuditedHardcodedValues()
    {
        var settings = GraphicsSettings.Faithful();

        Assert.Equal(1, settings.SampleCount);
        Assert.Equal(AlphaTestMode.LegacyCutoff, settings.AlphaTestMode);
        Assert.Equal(ColourSpaceMode.LegacyGamma, settings.ColourSpaceMode);
        Assert.Equal(4, settings.Anisotropy);
        Assert.Equal(0f, settings.MipLodBias);
        Assert.Equal(ShadowTier.Original, settings.ShadowTier);
        Assert.Equal(UiMode.LegacyArt, settings.UiMode);
        Assert.False(settings.RealisticProportions);
    }

    [Fact]
    public void EnhancedPresetDiffersOnlyOnShadowTier()
    {
        var faithful = GraphicsSettings.Faithful();
        var enhanced = GraphicsSettings.Enhanced();

        Assert.Equal(faithful.SampleCount, enhanced.SampleCount);
        Assert.Equal(faithful.AlphaTestMode, enhanced.AlphaTestMode);
        Assert.Equal(faithful.ColourSpaceMode, enhanced.ColourSpaceMode);
        Assert.Equal(faithful.Anisotropy, enhanced.Anisotropy);
        Assert.Equal(faithful.MipLodBias, enhanced.MipLodBias);
        Assert.Equal(ShadowTier.High, enhanced.ShadowTier);
        Assert.Equal(faithful.UiMode, enhanced.UiMode);
        Assert.Equal(faithful.RealisticProportions, enhanced.RealisticProportions);
    }

    [Fact]
    public void PresetFactoriesReturnDistinctInstances()
    {
        Assert.NotSame(GraphicsSettings.Faithful(), GraphicsSettings.Faithful());
        Assert.NotSame(GraphicsSettings.Faithful(), GraphicsSettings.Enhanced());
    }

    [Fact]
    public void GameDefaultsToFaithfulPreset()
    {
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
