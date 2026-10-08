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
        Assert.Equal(ShadowTier.Original, settings.ShadowTier);
        Assert.Equal(UiMode.LegacyArt, settings.UiMode);
        Assert.False(settings.RealisticProportions);
    }

    [Fact]
    public void EnhancedPresetDiffersOnlyOnSampleCountAndShadowTier()
    {
        var legacy = GraphicsSettings.Legacy();
        var enhanced = GraphicsSettings.Enhanced();

        // 4x MSAA landed with G4; every other knob stays Legacy until its owning phase.
        Assert.Equal(4, enhanced.SampleCount);
        Assert.Equal(legacy.AlphaTestMode, enhanced.AlphaTestMode);
        Assert.Equal(legacy.ColourSpaceMode, enhanced.ColourSpaceMode);
        Assert.Equal(legacy.Anisotropy, enhanced.Anisotropy);
        Assert.Equal(legacy.MipLodBias, enhanced.MipLodBias);
        Assert.Equal(ShadowTier.High, enhanced.ShadowTier);
        Assert.Equal(legacy.UiMode, enhanced.UiMode);
        Assert.Equal(legacy.RealisticProportions, enhanced.RealisticProportions);
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
