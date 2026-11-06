using System;
using System.IO;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Maps;

/// <summary>
/// Records whole-map heightmap lattice dimensions of installed legacy maps. Passes vacuously when
/// no corpus is installed, matching the MapFileTests corpus enumeration. Misalignment with the
/// 128-interval chunk grid and maps the reader cannot parse are reported, never failed.
/// </summary>
public sealed class LegacyMapLatticeProbeTests
{
    private const uint ChunkIntervals = 128;

    private const uint BorderWidthWithFullChunkCoverage = 6;

    private const uint MinimumLatticePointsForBorderSix = 129;

    private readonly ITestOutputHelper _output;

    public LegacyMapLatticeProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InstalledMapLatticeDimensionsSatisfyAdapterPreconditions()
    {
        var probed = 0;
        var aligned = 0;
        var unreadable = 0;
        var unparseable = 0;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            using var entryStream = OpenOrNull(entry);

            if (entryStream is null)
            {
                unreadable++;
                _output.WriteLine($"{entry.FilePath}: unreadable (dangling entry); not probed.");
                return;
            }

            MapFile mapFile;

            try
            {
                using var mapStream = MapFile.Decompress(entryStream);
                mapFile = MapFile.FromStream(mapStream);
            }
            catch (Exception exception)
            {
                unparseable++;
                _output.WriteLine($"{entry.FilePath}: unparseable ({exception.GetType().Name}: {exception.Message}); not probed.");
                return;
            }

            var heights = mapFile.HeightMapData;
            var width = heights.Width;
            var height = heights.Height;
            var border = heights.BorderWidth;
            var isAligned = (width - 1) % ChunkIntervals == 0 && (height - 1) % ChunkIntervals == 0;

            _output.WriteLine($"{entry.FilePath}: Width={width} Height={height} BorderWidth={border} Aligned128={isAligned}");

            Assert.True(width > 2 * border, $"{entry.FilePath}: Width {width} must exceed 2 * BorderWidth {border}.");
            Assert.True(height > 2 * border, $"{entry.FilePath}: Height {height} must exceed 2 * BorderWidth {border}.");

            if (border == BorderWidthWithFullChunkCoverage)
            {
                Assert.True(width >= MinimumLatticePointsForBorderSix, $"{entry.FilePath}: Width {width} is below {MinimumLatticePointsForBorderSix} with border {border}.");
                Assert.True(height >= MinimumLatticePointsForBorderSix, $"{entry.FilePath}: Height {height} is below {MinimumLatticePointsForBorderSix} with border {border}.");
            }

            probed++;

            if (isAligned)
            {
                aligned++;
            }
        });

        _output.WriteLine($"Probed {probed} installed .map files: {aligned} 128-aligned, {probed - aligned} not; {unparseable} unparseable; {unreadable} unreadable.");
    }

    private static Stream? OpenOrNull(FileSystemEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
