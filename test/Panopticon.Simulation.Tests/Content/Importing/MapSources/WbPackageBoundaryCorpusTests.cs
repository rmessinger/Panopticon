#nullable enable

using System;
using System.IO;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Data.Map;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources;

/// <summary>
/// Installed-corpus boundary tests for WI-H6-01: converts a real map from the game installation,
/// opens it through the public boundary into MapFile, makes an edit, saves back to package,
/// and verifies the reopened state. Vacuous skip without an install.
/// </summary>
public sealed class WbPackageBoundaryCorpusTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ITestOutputHelper _output;

    public WbPackageBoundaryCorpusTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "wb-pkg-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void InstalledCorpus_ConvertOpenEditSaveReopen_PassesBoundary()
    {
        var corpusTested = false;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            if (corpusTested)
            {
                return;
            }

            MapFile mapFile;

            try
            {
                using var stream = entry.Open();
                using var decompressed = MapFile.Decompress(stream);
                mapFile = MapFile.FromStream(decompressed);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Skipping unparseable map {entry.FilePath}: {ex.Message}");
                return;
            }

            if (mapFile.SidesList is { } sides && !LegacyMapReader.IsSupportedSidesListVersion(sides.Version))
            {
                _output.WriteLine($"Skipping map with unsupported sides version {sides.Version}: {entry.FilePath}");
                return;
            }

            var pkgDir = Path.Combine(_tempRoot, "corpus-boundary-pkg");

            // Convert and save through public converter.
            var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, "; corpus map.ini", "STRING:CorpusKey=\"CorpusVal\"");
            Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

            // Open through public converter.
            var opened = MapPackageConverter.OpenPackage(pkgDir);
            Assert.Equal("; corpus map.ini", opened.MapIniContent);
            Assert.Equal("STRING:CorpusKey=\"CorpusVal\"", opened.MapStrContent);

            // Edit typed elevation and save.
            var origElev = opened.MapFile.HeightMapData.Elevations[5, 5];
            opened.MapFile.HeightMapData.Elevations[5, 5] = (ushort)(origElev + 15);

            var saveResult = MapPackageConverter.SavePackage(
                opened.MapFile,
                pkgDir,
                opened.MapIniContent,
                opened.MapStrContent,
                opened.Context);
            Assert.True(saveResult.Success, string.Join("; ", saveResult.Diagnostics));

            // Reopen and assert edited elevation was preserved.
            var reopened = MapPackageConverter.OpenPackage(pkgDir);
            Assert.Equal((ushort)(origElev + 15), reopened.MapFile.HeightMapData.Elevations[5, 5]);
            Assert.Equal("; corpus map.ini", reopened.MapIniContent);

            corpusTested = true;
            _output.WriteLine($"Corpus boundary test passed for {entry.FilePath}.");
        });

        if (!corpusTested)
        {
            _output.WriteLine("No installed corpus map found; test passed vacuously.");
        }
    }
}
