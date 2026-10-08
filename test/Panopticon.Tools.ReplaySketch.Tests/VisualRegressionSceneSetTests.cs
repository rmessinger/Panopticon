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
            ["terrain-default", "terrain-grazing", "foliage-distance", "cutout-fences", "unit-silhouettes", "shadow-casters", "hud"],
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
        Assert.False(sceneSet.Scenes.Single(scene => scene.Name == "terrain-default").FullHud);
    }

    [Fact]
    public void LoadEmbedded_EverySceneProfileLoadsAndMatchesItsName()
    {
        var sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();

        foreach (var scene in sceneSet.Scenes)
        {
            var profile = CaptureProfileLoader.LoadEmbeddedCapture(scene.Profile);
            Assert.Equal($"vr-{scene.Name}", profile.Name);
            Assert.True(scene.CaptureFrame >= 1);
        }
    }
}
