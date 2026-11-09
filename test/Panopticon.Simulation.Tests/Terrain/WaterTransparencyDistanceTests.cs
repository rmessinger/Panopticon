using System.Text;
using Panopticon.Content;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Terrain;

public class WaterTransparencyDistanceTests
{
    private const float Tolerance = 1e-4f;
    private static readonly float S = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    [Fact]
    public void WaterTransparency_DepthAndReflectionPlaneZ_ConvertAtParseBoundary()
    {
        const string ini =
            """
            WaterTransparency
              TransparentWaterMinOpacity = 0.5
              TransparentWaterDepth = 10.0
              ReflectionPlaneZ = 20.0
            End
            """;

        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\Test.ini", ini);
        var assetStore = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();
        var parser = new IniParser(fileSystem.GetFile(@"Data\INI\Test.ini")!, assetStore, SageGame.CncGenerals, new IniDataContext(), Encoding.UTF8);
        parser.ParseFile();
        var water = assetStore.WaterTransparency.Current;

        Assert.Equal(0.5f, water.TransparentWaterMinOpacity);
        Assert.Equal(10.0f * S, water.TransparentWaterDepth, Tolerance);
        Assert.Equal(20.0f * S, water.ReflectionPlaneZ, Tolerance);
    }
}
