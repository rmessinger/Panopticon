using System;
using System.IO;
using Panopticon.Data.Map;
using Xunit;

namespace Panopticon.Tests.Data.Map;

/// <summary>
/// <see cref="BlendTileData.ReplaceTextures"/> (WI-B4b-23): in-place texture table
/// replacement for painted preview maps, leaving the CreateEmpty and CreateFromHeights
/// factories and their single-texture defaults untouched.
/// </summary>
public sealed class BlendTileDataReplaceTexturesTests
{
    [Fact]
    public void ReplaceTextures_RebuildsTableInPlace_KeepsTilesAndPassability()
    {
        var blend = BlendTileData.CreateUniform(9, 9);
        blend.Tiles[4, 4] = 5;
        blend.Impassability[2, 2] = true;

        blend.ReplaceTextures(["DirtMediumType1", "GrassType1"]);

        Assert.Equal((ushort)8, blend.Version);
        Assert.Equal(81u, blend.NumTiles);
        Assert.Equal(2, blend.Textures.Length);
        Assert.Equal("DirtMediumType1", blend.Textures[0].Name);
        Assert.Equal("GrassType1", blend.Textures[1].Name);
        Assert.All(blend.Textures, texture =>
        {
            Assert.Equal(0u, texture.CellStart);
            Assert.Equal(16u, texture.CellCount);
            Assert.Equal(4u, texture.CellSize);
        });

        Assert.Equal(5, blend.Tiles[4, 4]);
        Assert.True(blend.Impassability[2, 2]);

        // The derived index cache resets: texture 1 owns tile values [64, 127].
        Assert.Equal(128, blend.TextureIndices.Count);
        Assert.Equal(0, blend.TextureIndices[0].TextureIndex);
        Assert.Equal(1, blend.TextureIndices[64].TextureIndex);
    }

    [Fact]
    public void ReplaceTextures_RoundTripsThroughMapFile()
    {
        var map = MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 3);
        map.BlendTileData.ReplaceTextures(["DirtMediumType1", "GrassType1"]);
        map.BlendTileData.Tiles[0, 0] = 64;

        var reloaded = WriteAndReload(map);

        Assert.Equal(2, reloaded.BlendTileData.Textures.Length);
        Assert.Equal("DirtMediumType1", reloaded.BlendTileData.Textures[0].Name);
        Assert.Equal("GrassType1", reloaded.BlendTileData.Textures[1].Name);
        Assert.Equal(64, reloaded.BlendTileData.Tiles[0, 0]);
    }

    [Fact]
    public void ReplaceTextures_RejectsInvalidArguments()
    {
        var blend = BlendTileData.CreateUniform(9, 9);

        Assert.Throws<ArgumentNullException>(() => blend.ReplaceTextures(null!));
        Assert.Throws<ArgumentException>(() => blend.ReplaceTextures([]));
        Assert.Throws<ArgumentException>(() => blend.ReplaceTextures(["GrassType1", ""]));
        Assert.Throws<ArgumentException>(() => blend.ReplaceTextures(["GrassType1", "GrassType1"]));
    }

    [Fact]
    public void OldFactories_KeepSingleTextureDefaults()
    {
        var uniform = BlendTileData.CreateUniform(9, 9);
        Assert.Single(uniform.Textures);
        Assert.Equal(BlendTileData.DefaultTextureName, uniform.Textures[0].Name);

        var fromHeights = MapFile.CreateFromHeights(new ushort[9, 9], borderWidth: 3);
        Assert.Single(fromHeights.BlendTileData.Textures);
        Assert.Equal(BlendTileData.DefaultTextureName, fromHeights.BlendTileData.Textures[0].Name);
    }

    private static MapFile WriteAndReload(MapFile map)
    {
        byte[] written;
        using (var stream = new MemoryStream())
        {
            map.WriteTo(stream);
            written = stream.ToArray();
        }

        return MapFile.FromStream(new MemoryStream(written));
    }
}
