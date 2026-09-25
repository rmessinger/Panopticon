using System.Text;
using Panopticon.Content;
using Panopticon.Data.Ini;
using Panopticon.FX;
using Panopticon.IO;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.FX;

public class TracerFXNuggetTests
{
    private static FXList ParseFxList(string contents)
    {
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(@"Data\INI\TestTracer.ini", contents);

        var assetStore = new AssetStore(
            SageGame.CncGenerals,
            fileSystem,
            language: null,
            graphicsDevice: null,
            standardGraphicsResources: null,
            shaderResources: null,
            shaderSetStore: null,
            OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();

        var parser = new IniParser(
            fileSystem.GetFile(@"Data\INI\TestTracer.ini")!,
            assetStore,
            SageGame.CncGenerals,
            new IniDataContext(),
            Encoding.UTF8);
        parser.ParseFile();

        return assetStore.FXLists.GetByName("TestTracerFX");
    }

    [Fact]
    public void TracerNugget_ParsesAllFields()
    {
        var fxList = ParseFxList("""
            FXList TestTracerFX
              Tracer
                DecayAt = 0.5
                Length = 20
                Width = 0.5
                Color = R:230 G:204 B:179
                Speed = 600
                Probability = 0.5
              End
            End
            """);

        var nugget = Assert.IsType<TracerFXNugget>(Assert.Single(fxList.Nuggets));

        Assert.Equal(0.5f, nugget.DecayAt);
        Assert.Equal(20f, nugget.Length);
        Assert.Equal(0.5f, nugget.Width);
        Assert.Equal(600, nugget.Speed);
        Assert.Equal(0.5f, nugget.Probability);
        Assert.Equal(230, nugget.Color.R);
        Assert.Equal(204, nugget.Color.G);
        Assert.Equal(179, nugget.Color.B);
    }

    [Fact]
    public void TracerNugget_AbsentFields_DefaultToAlwaysFireAndNoEarlyDecay()
    {
        // WeaponFX_GenericTankGun's Tracer omits Speed and Probability: the tracer always
        // fires, at the default speed.
        var fxList = ParseFxList("""
            FXList TestTracerFX
              Tracer
                Length = 20
                Width = 0.5
                Color = R:230 G:204 B:179
              End
            End
            """);

        var nugget = Assert.IsType<TracerFXNugget>(Assert.Single(fxList.Nuggets));

        Assert.Equal(1.0f, nugget.Probability);
        Assert.Equal(1.0f, nugget.DecayAt);
        Assert.Equal(0, nugget.Speed);
    }
}
