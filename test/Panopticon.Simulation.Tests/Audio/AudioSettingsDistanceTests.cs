using System.Text;
using Panopticon.Content;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Audio;

public class AudioSettingsDistanceTests
{
    [Fact]
    public void AudioSettings_RangeAndZoomFadeDistances_ConvertAtParseBoundary()
    {
        const string ini =
            """
            AudioSettings
              GlobalMinRange = 50.0
              GlobalMaxRange = 500.0
              ZoomFadeDistanceForMaxEffect = 100.0
              ZoomFadeZeroEffectEdgeLength = 10.0
              ZoomFadeFullEffectEdgeLength = 20.0
            End
            """;

        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\Test.ini", ini);
        var assetStore = new AssetStore(SageGame.CncGenerals, fileSystem, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();
        var parser = new IniParser(fileSystem.GetFile(@"Data\INI\Test.ini")!, assetStore, SageGame.CncGenerals, new IniDataContext(), Encoding.UTF8);
        parser.ParseFile();
        var audio = assetStore.AudioSettings.Current;

        Assert.Equal(15.24f, audio.GlobalMinRange, precision: 4);
        Assert.Equal(152.4f, audio.GlobalMaxRange, precision: 4);
        Assert.Equal(30.48f, audio.ZoomFadeDistanceForMaxEffect, precision: 4);
        Assert.Equal(3.048f, audio.ZoomFadeZeroEffectEdgeLength, precision: 4);
        Assert.Equal(6.096f, audio.ZoomFadeFullEffectEdgeLength, precision: 4);
    }
}
