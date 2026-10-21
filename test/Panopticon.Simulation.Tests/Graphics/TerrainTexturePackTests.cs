using System;
using System.IO;
using System.Linq;
using Panopticon.Content;
using Panopticon.IO;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class TerrainTexturePackTests
{
    [Fact]
    public void BakeAndLoad_ReusesCompletePackWithoutRegenerating()
    {
        var root = NewRoot();
        var pack = new TerrainTexturePack(root);
        var source = Source(128);
        Assert.True(pack.Bake(source, 4));
        Assert.False(pack.Bake(source, 4));
        Assert.True(pack.TryLoad(source, 4, out var texture, out var reason), reason);
        try
        {
            Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm_SRgb, texture.Format);
            Assert.Equal(new[] { 4, 2, 1 }, texture.Images.Select(i => i.Width));
            Assert.All(texture.Images, image => Assert.Equal(128, image[0, 0].R));
        }
        finally
        {
            foreach (var image in texture.Images)
            {
                image.Dispose();
            }
        }
    }

    [Fact]
    public void ChangedSourceAndDifferentArraySize_DoNotUseStaleData()
    {
        var pack = new TerrainTexturePack(NewRoot());
        Assert.True(pack.Bake(Source(128), 4));
        Assert.False(pack.TryLoad(Source(192), 4, out _, out _));
        Assert.False(pack.TryLoad(Source(128), 8, out _, out _));
    }

    [Fact]
    public void CorruptMipIsRejected_AndBakeDoesNotOverwriteIt()
    {
        var root = NewRoot();
        var pack = new TerrainTexturePack(root);
        var source = Source(128);
        pack.Bake(source, 4);
        var mip = Directory.GetFiles(root, "mip_00.png", SearchOption.AllDirectories).Single();
        File.WriteAllText(mip, "corrupt test mip");
        Assert.False(pack.TryLoad(source, 4, out _, out var reason));
        Assert.Contains("checksum", reason);
        Assert.Throws<InvalidDataException>(() => pack.Bake(source, 4));
        Assert.Equal("corrupt test mip", File.ReadAllText(mip));
    }

    [Fact]
    public void InvalidManifestAndMissingLevels_AreRejected()
    {
        var root = NewRoot();
        var pack = new TerrainTexturePack(root);
        var source = Source(128);
        pack.Bake(source, 4);
        var mip = Directory.GetFiles(root, "mip_01.png", SearchOption.AllDirectories).Single();
        File.Delete(mip);
        Assert.False(pack.TryLoad(source, 4, out _, out _));
        var manifest = Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(manifest, "{}");
        Assert.False(pack.TryLoad(source, 4, out _, out _));
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "opencode", "terrain-pack-tests", Guid.NewGuid().ToString("N"));

    private static FileSystemEntry Source(byte value)
    {
        var bytes = new byte[18 + 4 * 4 * 4];
        bytes[2] = 2;
        bytes[12] = bytes[14] = 4;
        bytes[16] = 32;
        bytes[17] = 0x28;
        for (var offset = 18; offset < bytes.Length; offset += 4)
        {
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = value;
            bytes[offset + 3] = 255;
        }

        return new FileSystemEntry(null!, "Art/Terrain/test.tga", (uint)bytes.Length, () => new MemoryStream(bytes, writable: false));
    }
}
