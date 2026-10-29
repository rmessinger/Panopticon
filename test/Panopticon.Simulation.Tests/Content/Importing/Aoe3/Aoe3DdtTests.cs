using System;
using System.IO;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.FileFormats.Bar;
using Panopticon.IO;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Decodes the C10 reference DDT textures of the installed AoE3 DE Game
/// directory. Interactive: requires AOE3DE_PATH pointing at the install root
/// containing Game.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Aoe3DdtTests
{
    private readonly ITestOutputHelper _output;

    public Aoe3DdtTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("BaseColor")]
    [InlineData("Normals")]
    [InlineData("Masks")]
    [InlineData("Details")]
    public void MusketeerMatATextureDecodes(string suffix)
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var path = $@"Art\units\infantry_ranged\musketeer\textures\musketeer_4age_matA_{suffix}.ddt";

        DdtFile ddt;
        using (var stream = fileSystem.GetFile(path)!.Open())
        {
            ddt = DdtFile.Read(stream);
        }

        Assert.Equal(DdtFormat.Bc1, ddt.Format);
        Assert.Equal(10, ddt.MipCount);
        Assert.Equal(1024u, ddt.Width);
        Assert.Equal(1024u, ddt.Height);
        Assert.Equal(524288u, ddt.Mips[0].Size);
        _output.WriteLine($"{suffix}: format {ddt.Format}, {ddt.MipCount} mips, {ddt.Width}x{ddt.Height}");

        var decoded = ImportedImageDecoder.Decode(
            ImportedImageSource.CreateVirtualPath(path.Replace('\\', '/')),
            fileSystem);

        Assert.Equal(1024, decoded.Width);
        Assert.Equal(1024, decoded.Height);
        Assert.Equal(1024 * 1024 * 4, decoded.RgbaPixels.Length);
    }

    [Fact]
    public void MusketeerBc2AndBc3TexturesDecode()
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var uiTexture = DecodeAndAssert(fileSystem, @"Art\units\infantry_ranged\musketeer\musketeer_4age.ddt", DdtFormat.Bc2, 64);
        var bumpTexture = DecodeAndAssert(fileSystem, @"Art\units\infantry_ranged\musketeer\musketeer_4age_bump.ddt", DdtFormat.Bc3, 64);

        Assert.NotEqual(uiTexture, bumpTexture);
    }

    private static byte[] DecodeAndAssert(BarFileSystem fileSystem, string path, DdtFormat expectedFormat, int expectedSize)
    {
        DdtFile ddt;
        using (var stream = fileSystem.GetFile(path)!.Open())
        {
            ddt = DdtFile.Read(stream);
        }

        Assert.Equal(expectedFormat, ddt.Format);
        Assert.Equal((uint)expectedSize, ddt.Width);
        Assert.Equal((uint)expectedSize, ddt.Height);

        var decoded = ImportedImageDecoder.Decode(
            ImportedImageSource.CreateVirtualPath(path.Replace('\\', '/')),
            fileSystem);

        Assert.Equal(expectedSize, decoded.Width);
        return decoded.RgbaPixels.ToArray();
    }
}
