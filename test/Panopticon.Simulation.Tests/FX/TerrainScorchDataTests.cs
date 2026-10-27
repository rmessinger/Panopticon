using System;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Content;
using Panopticon.Data.Ini;
using Panopticon.Data.Map;
using Panopticon.FX;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.FX;

public sealed class TerrainScorchDataTests
{
    private readonly ITestOutputHelper _output;

    public TerrainScorchDataTests(ITestOutputHelper output) => _output = output;

    [GameFact(SageGame.CncGenerals)]
    public void GeneralsScorches_ParseSupportedTypes() => AssertInstalledScorches(SageGame.CncGenerals);

    [GameFact(SageGame.CncGeneralsZeroHour)]
    public void ZeroHourScorches_ParseSupportedTypes() => AssertInstalledScorches(SageGame.CncGeneralsZeroHour);

    [GameFact(SageGame.CncGenerals)]
    public void InstalledMaps_RecordScorchProducers()
    {
        using var files = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
        var total = 0;
        foreach (var file in files.GetFilesInDirectory("maps", "*.map", SearchOption.AllDirectories))
        {
            MapFile map;
            try
            {
                map = MapFile.FromFileSystemEntry(file);
            }
            catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
            {
                _output.WriteLine($"Map parser gap: {file.FilePath}");
                continue;
            }

            var count = map.ObjectsList.Objects.Count(mapObject =>
                mapObject.Properties.GetPropOrNull("scorchType")?.Key.PropertyType == AssetPropertyType.Integer);
            total += count;
            if (count > 0)
            {
                _output.WriteLine($"{file.FilePath}: {count} scorches");
            }
        }

        Assert.True(total > 0);
    }

    private void AssertInstalledScorches(SageGame game)
    {
        using var files = InstalledFilesTestData.GetInstallation(game).CreateFileSystem();
        var assets = new AssetStore(game, files, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        assets.PushScope();
        foreach (var file in files.GetFilesInDirectory("Data/INI", "*.ini", SearchOption.AllDirectories))
        {
            if (!file.FilePath.Contains("fxlist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            new IniParser(file, assets, game, new IniDataContext(), Encoding.UTF8).ParseFile();
        }

        var nuggets = assets.FXLists.SelectMany(list => list.Nuggets).OfType<TerrainScorchFXNugget>().ToArray();
        _output.WriteLine($"{game}: {nuggets.Length} TerrainScorch nuggets");
        Assert.Equal(game == SageGame.CncGenerals ? 30 : 46, nuggets.Length);
        Assert.All(nuggets, nugget =>
        {
            Assert.InRange((int)nugget.Type, -1, 4);
            Assert.True(nugget.Radius > 0);
        });
    }
}
