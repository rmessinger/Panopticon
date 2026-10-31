using System;
using System.IO;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Scripting;
using Xunit;

namespace Panopticon.Tests.Data.Map;

/// <summary>
/// <see cref="MapFile.CreateFromHeights"/>: complete playable map from a raw elevation grid,
/// bit-exact 16-bit height round-trip, (east, north, 0) start waypoints, and
/// <see cref="MapFile.CreateEmpty"/> keeping its 8-bit semantics.
/// </summary>
public sealed class MapFileCreateFromHeightsTests
{
    [Fact]
    public void FullRangeHeights_RoundTripBitExactly()
    {
        const int size = 257;
        const uint border = 3;
        var heights = new ushort[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // Spans the ushort range, far above the 8-bit ceiling of 255.
                heights[x, y] = (ushort)((x * 601u + y * 4051u) % 65536u);
            }
        }

        heights[0, 0] = 0;
        heights[size - 1, size - 1] = ushort.MaxValue;

        var map = MapFile.CreateFromHeights(heights, border);
        Assert.True(map.HeightMapData.ElevationsAre16Bit);

        var reloaded = WriteAndReload(map);

        Assert.Equal((ushort)5, reloaded.HeightMapData.Version);
        Assert.True(reloaded.HeightMapData.ElevationsAre16Bit);
        Assert.Equal(0.0390625f, reloaded.HeightMapData.VerticalScale);
        Assert.Equal((uint)size, reloaded.HeightMapData.Width);
        Assert.Equal((uint)size, reloaded.HeightMapData.Height);
        Assert.Equal(border, reloaded.HeightMapData.BorderWidth);
        Assert.Equal(66049u, reloaded.HeightMapData.Area);

        var sawAbove8Bit = false;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Assert.Equal(heights[x, y], reloaded.HeightMapData.Elevations[x, y]);
                sawAbove8Bit |= reloaded.HeightMapData.Elevations[x, y] > 255;
            }
        }

        Assert.True(sawAbove8Bit);
    }

    [Fact]
    public void ComposedMap_CarriesCompletePlayableDefaults()
    {
        var reloaded = WriteAndReload(MapFile.CreateFromHeights(new ushort[131, 79], borderWidth: 3));

        Assert.NotNull(reloaded.HeightMapData);
        Assert.NotNull(reloaded.BlendTileData);
        Assert.NotNull(reloaded.WorldInfo);
        Assert.NotNull(reloaded.SidesList);
        Assert.NotNull(reloaded.ObjectsList);
        Assert.NotNull(reloaded.GlobalLighting);
        Assert.NotNull(reloaded.WaypointsList);

        Assert.Equal((ushort)8, reloaded.BlendTileData.Version);
        Assert.Equal(reloaded.HeightMapData.Width * reloaded.HeightMapData.Height, reloaded.BlendTileData.NumTiles);
        Assert.Single(reloaded.BlendTileData.Textures);
        Assert.Equal(BlendTileData.DefaultTextureName, reloaded.BlendTileData.Textures[0].Name);
        foreach (var impassable in reloaded.BlendTileData.Impassability)
        {
            Assert.False(impassable);
        }

        // Load-bearing order: neutral, civilian, then faction slots.
        var players = reloaded.SidesList.Players;
        Assert.Equal(4, players.Count);
        Assert.Equal(string.Empty, players[0].Name);
        Assert.Equal("FactionCivilian", players[1].Faction);
        Assert.Equal("FactionAmerica", players[2].Faction);
        Assert.Equal("FactionGLA", players[3].Faction);
    }

    [Fact]
    public void StartWaypoints_AreEastNorthAtZeroHeight_InsidePlayableBounds()
    {
        const uint border = 3;
        const int width = 131;
        const int height = 79;
        var heights = new ushort[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Non-flat grid: a marker that bakes in height or swaps axes cannot match.
                heights[x, y] = (ushort)(1000 + 5 * x + 13 * y);
            }
        }

        var reloaded = WriteAndReload(MapFile.CreateFromHeights(heights, border));

        var objects = reloaded.ObjectsList.Objects;
        Assert.Equal(2, objects.Length);
        Assert.All(objects, o => Assert.Equal(Waypoint.ObjectTypeName, o.TypeName));

        Assert.Equal("Player_1_Start", (string)objects[0].Properties["waypointName"].Value);
        Assert.Equal("Player_2_Start", (string)objects[1].Properties["waypointName"].Value);
        Assert.Equal(1, (int)objects[0].Properties["waypointID"].Value);
        Assert.Equal(2, (int)objects[1].Properties["waypointID"].Value);

        // (east, north, 0): Scene3D adds terrain height to Z itself. Exact values pin the
        // axes (124 and 72 playable samples, 25% / 75% in world units), so an axis swap,
        // border offset, or baked height fails on this non-square, non-flat grid.
        Assert.Equal(new Vector3(310f, 180f, 0f), objects[0].Position);
        Assert.Equal(new Vector3(930f, 540f, 0f), objects[1].Position);
    }

    [Fact]
    public void CreateEmpty_Keeps8BitSemantics()
    {
        var map = MapFile.CreateEmpty(playableWidth: 16, playableHeight: 16, borderWidth: 2, initialHeight: 200);
        Assert.False(map.HeightMapData.ElevationsAre16Bit);
        Assert.Equal(0.625f, map.HeightMapData.VerticalScale);

        var reloaded = WriteAndReload(map);

        Assert.False(reloaded.HeightMapData.ElevationsAre16Bit);
        Assert.Equal(0.625f, reloaded.HeightMapData.VerticalScale);
        foreach (var elevation in reloaded.HeightMapData.Elevations)
        {
            Assert.Equal(200, elevation);
        }
    }

    [Fact]
    public void CustomTerrainTexture_RoundTrips()
    {
        var reloaded = WriteAndReload(MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 3, textureName: "DirtMediumType1"));

        Assert.Single(reloaded.BlendTileData.Textures);
        Assert.Equal("DirtMediumType1", reloaded.BlendTileData.Textures[0].Name);
    }

    [Fact]
    public void InvalidSizeBorderOrTextureName_Throws()
    {
        // 1x1 with no border leaves no playable area.
        Assert.Throws<ArgumentException>(() => MapFile.CreateFromHeights(new ushort[1, 1], borderWidth: 0));

        // Border consumes the whole grid: 9 - 2*4 - 1 = 0 playable samples.
        Assert.Throws<ArgumentException>(() => MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 4));

        Assert.Throws<ArgumentNullException>(() => MapFile.CreateFromHeights(null!, borderWidth: 3));
        Assert.Throws<ArgumentException>(() => MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 3, textureName: ""));
    }

    [Fact]
    public void HugeBorder_ThrowsInsteadOfOverflowing()
    {
        // borderWidth * 2 + 2 wraps around uint at and above 2^31; the guard must widen.
        Assert.Throws<ArgumentException>(() => MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: uint.MaxValue));
        Assert.Throws<ArgumentException>(() => MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 2147483648u));
    }

    private static MapFile WriteAndReload(MapFile map)
    {
        // WriteTo closes the stream it writes to, so the bytes are buffered for re-reading.
        byte[] written;
        using (var stream = new MemoryStream())
        {
            map.WriteTo(stream);
            written = stream.ToArray();
        }

        return MapFile.FromStream(new MemoryStream(written));
    }
}
