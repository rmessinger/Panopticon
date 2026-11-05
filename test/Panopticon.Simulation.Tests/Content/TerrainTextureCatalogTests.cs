using System;
using System.IO;
using System.Linq;
using Panopticon.Content;
using Panopticon.Data;
using Panopticon.Mods.BuiltIn;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Content;

public class TerrainTextureCatalogTests
{
    [GameFact(SageGame.CncGenerals)]
    public void LoadNames_ReturnsCanonicalInstalledNames_WithoutGameBoot()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        var names = TerrainTextureCatalog.LoadNames(installation);

        Assert.NotEmpty(names);
        Assert.Equal(names.Distinct().Count(), names.Count);
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), names);
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));

        // Immutable snapshot: callers cannot mutate the catalog through the returned
        // reference (no live array escapes the loader).
        Assert.IsNotType<string[]>(names);

        // Anchors from the real install's Data/INI/Terrain.ini (INI.big).
        Assert.Contains("AsphaltType1", names);
        Assert.Contains("CliffMediumType1", names);
    }

    [Fact]
    public void LoadNames_MissingTerrainIni_Throws()
    {
        var emptyRoot = Path.Combine(Path.GetTempPath(), "opencode", "terrain-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            var installation = new GameInstallation(
                GameDefinition.FromGame(SageGame.CncGenerals),
                emptyRoot);

            Assert.Throws<FileNotFoundException>(() => TerrainTextureCatalog.LoadNames(installation));
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }
}
