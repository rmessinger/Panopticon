using System;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class TerrainZoomPathTests
{
    [Fact]
    public void Catalog_AdvertisesHeadlessProofAndRegressionExitCode()
    {
        var scenario = Assert.Single(HarnessScenarioCatalog.Scenarios(), s => s.Id == nameof(BuiltInScenario.TerrainZoomProof));
        Assert.Equal(HarnessRunModes.Headless, scenario.Modes);
        Assert.True(scenario.ExitCodes.ContainsKey(2));
    }

    [Fact]
    public void ZoomOutAndBack_HasExactEndpointsAndSymmetry()
    {
        Assert.Equal(TerrainZoomPath.NearHeight, TerrainZoomPath.HeightAt(0));
        Assert.Equal(TerrainZoomPath.FarHeight, TerrainZoomPath.HeightAt(TerrainZoomPath.FrameCount / 2));
        for (var frame = 0; frame < TerrainZoomPath.FrameCount; frame++)
        {
            Assert.Equal(TerrainZoomPath.HeightAt(frame), TerrainZoomPath.HeightAt(TerrainZoomPath.FrameCount - 1 - frame));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(TerrainZoomPath.FrameCount)]
    public void InvalidFrame_IsRejected(int frame) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainZoomPath.HeightAt(frame));
}
