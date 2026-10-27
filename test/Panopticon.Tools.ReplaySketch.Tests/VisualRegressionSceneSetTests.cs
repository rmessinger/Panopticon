using System.Linq;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class VisualRegressionSceneSetTests
{
    [Fact]
    public void LoadEmbedded_ReturnsAllScenesInOrder()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        var names = sceneSet.Scenes.Select(scene => scene.Name).ToArray();
        Assert.Equal(
            ["terrain-scorch", "terrain-default", "terrain-grazing", "foliage-distance", "cutout-fences", "unit-silhouettes", "shadow-casters", "hud", "hud-4x", "proportions", "proportions-on"],
            names);
    }

    [Fact]
    public void LoadEmbedded_SilhouetteScene_DeclaresRegions()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        var silhouettes = sceneSet.Scenes.Single(scene => scene.Name == "unit-silhouettes");
        var region = Assert.Single(silhouettes.SilhouetteRegions);
        Assert.True(region.X >= 0f && region.Y >= 0f && region.Width > 0f && region.Height > 0f);
        Assert.True(region.X + region.Width <= 1f && region.Y + region.Height <= 1f);
    }

    [Fact]
    public void LoadEmbedded_HudScene_RequestsFullHud()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        var hud = sceneSet.Scenes.Single(scene => scene.Name == "hud");
        Assert.True(hud.FullHud);

        var hud4x = sceneSet.Scenes.Single(scene => scene.Name == "hud-4x");
        Assert.True(hud4x.FullHud);
        Assert.Equal(4, hud4x.ViewportScale);

        Assert.False(sceneSet.Scenes.Single(scene => scene.Name == "terrain-default").FullHud);
        Assert.Equal(1, sceneSet.Scenes.Single(scene => scene.Name == "terrain-default").ViewportScale);
    }

    [Fact]
    public void LoadEmbedded_ProportionsScenes_ToggleOnlyTheProportionsFlag()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        var off = sceneSet.Scenes.Single(scene => scene.Name == "proportions");
        var on = sceneSet.Scenes.Single(scene => scene.Name == "proportions-on");

        Assert.False(off.RealisticProportions);
        Assert.True(on.RealisticProportions);
        Assert.Equal(off.Profile, on.Profile);
    }

    [Fact]
    public void LoadEmbedded_EverySceneProfileLoadsAndMatchesItsName()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        foreach (var scene in sceneSet.Scenes)
        {
            var profile = CaptureProfileLoader.LoadEmbeddedCapture(scene.Profile);

            // Scaled variants reuse their base scene's profile (hud-4x -> vr-hud), and the
            // proportions-on variant reuses vr-proportions.
            var baseName = scene.Name;
            if (scene.ViewportScale != 1 || baseName.EndsWith("-on"))
            {
                baseName = scene.Name.Split('-')[0];
            }

            Assert.Equal($"vr-{baseName}", profile.Name);
            Assert.True(scene.CaptureFrame >= 1);
        }
    }
}
