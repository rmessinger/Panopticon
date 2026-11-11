using System;
using System.IO;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-B4a-06: large-map presets, 16-bit elevation defaults, and array-limit-only size validation.
/// </summary>
public sealed class NewMapWizardPresetTests
{
    private const int BorderWidth = 6;
    private const int CentreIndex = BorderWidth + 16;

    [Fact]
    public void Presets_ListNamedSizesWith16BitFlags()
    {
        var expected = new[]
        {
            new NewMapPreset("Standard", 200, 200, false),
            new NewMapPreset("Large", 512, 512, true),
            new NewMapPreset("Very Large", 1024, 1024, true),
            new NewMapPreset("Huge", 2048, 2048, true),
        };

        Assert.Equal(expected, NewMapFactory.Presets);
    }

    [Fact]
    public void LargePreset_CreatesSixteenBitMapFilledWithInitialHeight()
    {
        var large = NewMapFactory.Presets[1];
        var map = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = large.PlayableWidth,
            PlayableHeight = large.PlayableHeight,
            InitialHeight = 300,
            Use16BitElevation = large.Use16BitElevation,
        });

        var heightMap = map.MapFile.HeightMapData;
        var expectedSide = (uint)(large.PlayableWidth + 2 * BorderWidth + 1);
        Assert.True(heightMap.ElevationsAre16Bit);
        Assert.Equal(expectedSide, heightMap.Width);
        Assert.Equal(expectedSide, heightMap.Height);

        var mismatches = 0;
        foreach (var elevation in heightMap.Elevations)
        {
            if (elevation != 300)
            {
                mismatches++;
            }
        }

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void StandardPreset_CreatesEightBitMap()
    {
        var standard = NewMapFactory.Presets[0];
        var map = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = standard.PlayableWidth,
            PlayableHeight = standard.PlayableHeight,
            InitialHeight = 50,
            Use16BitElevation = standard.Use16BitElevation,
        });

        Assert.False(map.MapFile.HeightMapData.ElevationsAre16Bit);
    }

    [Fact]
    public void SixteenBitMap_KeepsElevationAboveByteRangeThroughLegacySave()
    {
        var map = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 1000,
            Use16BitElevation = true,
        });
        var path = TempMapPath();

        try
        {
            map.SaveAs(path);

            using var stream = File.OpenRead(path);
            var reopened = EditorMap.Open(MapFile.FromStream(stream), path);
            Assert.Equal((ushort)1000, reopened.MapFile.HeightMapData.Elevations[CentreIndex, CentreIndex]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EightBitMap_TruncatesElevationAboveByteRangeThroughLegacySave()
    {
        var mapFile = MapFile.CreateEmpty(32, 32, BorderWidth, 1000);
        var path = TempMapPath();

        try
        {
            using (var output = File.Create(path))
            {
                mapFile.WriteTo(output);
            }

            using var stream = File.OpenRead(path);
            var reopened = MapFile.FromStream(stream);
            Assert.Equal((ushort)(1000 & 0xFF), reopened.HeightMapData.Elevations[CentreIndex, CentreIndex]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryValidate_AcceptsLargeCustomSizesWithoutAllocating()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Lattice 23 x 93368854 = 2147483642 cells, just under int.MaxValue.
        var accepted4096 = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = 4096, PlayableHeight = 4096, InitialHeight = 0, Use16BitElevation = true },
            out var error4096);
        var acceptedBoundary = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = 10, PlayableHeight = 93368841, InitialHeight = 0, Use16BitElevation = true },
            out var errorBoundary);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(accepted4096);
        Assert.Equal(string.Empty, error4096);
        Assert.True(acceptedBoundary);
        Assert.Equal(string.Empty, errorBoundary);
        Assert.True(allocated < 64 * 1024, $"TryValidate allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9)]
    public void TryValidate_RejectsWidthBelowMinimum(int width)
    {
        var accepted = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = width, PlayableHeight = 200, InitialHeight = 0 },
            out var error);

        Assert.False(accepted);
        Assert.Equal("Width must be at least 10.", error);
    }

    [Fact]
    public void TryValidate_RejectsBorderBelowMinimum()
    {
        var accepted = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = 200, PlayableHeight = 200, BorderWidth = 1, InitialHeight = 0 },
            out var error);

        Assert.False(accepted);
        Assert.Equal("Border width must be at least 2.", error);
    }

    [Fact]
    public void TryValidate_RejectsIntMaxWidthWithArrayLimitError()
    {
        var accepted = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = int.MaxValue, PlayableHeight = 200, InitialHeight = 0 },
            out var error);

        Assert.False(accepted);
        Assert.Contains("per-axis array limit", error);
    }

    [Fact]
    public void TryValidate_RejectsTotalCellsAboveArrayLimit()
    {
        var accepted = NewMapFactory.TryValidate(
            new NewMapParameters { PlayableWidth = 1_000_000, PlayableHeight = 1_000_000, InitialHeight = 0 },
            out var error);

        Assert.False(accepted);
        Assert.Contains("total array limit", error);
    }

    [Fact]
    public void TryValidate_ReportsEachRejectionReasonDistinctly()
    {
        NewMapFactory.TryValidate(new NewMapParameters { PlayableWidth = 9, PlayableHeight = 200, InitialHeight = 0 }, out var widthError);
        NewMapFactory.TryValidate(new NewMapParameters { PlayableWidth = 200, PlayableHeight = 200, BorderWidth = 1, InitialHeight = 0 }, out var borderError);
        NewMapFactory.TryValidate(new NewMapParameters { PlayableWidth = int.MaxValue, PlayableHeight = 200, InitialHeight = 0 }, out var limitError);

        Assert.NotEqual(widthError, borderError);
        Assert.NotEqual(widthError, limitError);
        Assert.NotEqual(borderError, limitError);
    }

    [Fact]
    public void Create_RejectsIntMaxWidthWithArgumentException()
    {
        Assert.Throws<ArgumentException>(() => NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = int.MaxValue,
            PlayableHeight = 200,
            InitialHeight = 0,
        }));
    }

    [Fact]
    public void EstimateResidentBytes_MatchesLatticeCellsTimesPerCellBytes()
    {
        // Lattice (10 + 2 * 2 + 1) x (10 + 2 * 2 + 1) = 225 cells.
        Assert.Equal(225L * 29, NewMapFactory.EstimateResidentBytes(10, 10, 2));
    }

    [Fact]
    public void EstimateResidentBytes_IncreasesWithSize()
    {
        var sizes = new[] { 10, 200, 512, 1024, 2048, 4096 };
        var previous = 0L;

        foreach (var size in sizes)
        {
            var estimate = NewMapFactory.EstimateResidentBytes(size, size, BorderWidth);
            Assert.True(estimate > previous, $"Estimate for {size} did not exceed {previous}.");
            previous = estimate;
        }
    }

    private static string TempMapPath()
    {
        return Path.Combine(Path.GetTempPath(), $"panopticon_wizard_preset_{Guid.NewGuid():N}.map");
    }
}
