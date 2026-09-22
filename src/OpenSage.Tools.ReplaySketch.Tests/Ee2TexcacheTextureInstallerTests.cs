using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Nif;
using OpenSage.IO;
using OpenSage.Tools.TestHarness;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Non-interactive coverage for the texcache plumbing: name resolution through
/// the import file system and the DXT format mapping. GPU decode/upload paths
/// are exercised by the Ee2NifProof scenario against the installed archives.
/// </summary>
public sealed class Ee2TexcacheTextureInstallerTests
{
    private static AssetImportContext ContextWithFiles(params (string Path, string Content)[] files)
    {
        return new AssetImportContext(new MemoryFileSystem(files), language: null);
    }

    private sealed class MemoryFileSystem((string Path, string Content)[] files) : FileSystem
    {
        private readonly Dictionary<string, string> _files =
            new(files.ToDictionary(f => FileSystem.NormalizeFilePath(f.Path), f => f.Content, StringComparer.OrdinalIgnoreCase));

        public override FileSystemEntry? GetFile(string filePath)
        {
            var normalized = FileSystem.NormalizeFilePath(filePath);
            return _files.TryGetValue(normalized, out var content)
                ? new FileSystemEntry(this, normalized, (uint)content.Length, () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
                : null;
        }

        public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
            string directoryPath,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            yield break;
        }
    }

    [Theory]
    [InlineData("nwg1_galley.tga", "cache/texcache/nwg1_galley.texcache")]
    [InlineData("ab11_TwinEngineBomber.tga", "cache/texcache/ab11_TwinEngineBomber.texcache")]
    [InlineData("subdir/model_d03.tga", "cache/texcache/model_d03.texcache")]
    public void ResolvesTexcachePathFromSourceName(string sourceName, string expectedPath)
    {
        var context = ContextWithFiles((expectedPath, "x"));
        Assert.True(NifMaterialConverter.TryResolveTexcache(sourceName, context, out var resolved));
        Assert.Equal(FileSystem.NormalizeFilePath(expectedPath), FileSystem.NormalizeFilePath(resolved));
    }

    [Fact]
    public void MissingTexcacheDoesNotResolve()
    {
        var context = ContextWithFiles();
        Assert.False(NifMaterialConverter.TryResolveTexcache("absent.tga", context, out _));
    }

    [Fact]
    public void DxtFormatsMapToBlockCompressedFormats()
    {
        Assert.Equal(
            Veldrid.PixelFormat.BC1_Rgba_UNorm,
            Ee2TexcacheTextureInstaller.MapPixelFormat(OpenSage.FileFormats.Nif.NifPixelData.FormatDxt1));
        Assert.Equal(
            Veldrid.PixelFormat.BC2_UNorm,
            Ee2TexcacheTextureInstaller.MapPixelFormat(OpenSage.FileFormats.Nif.NifPixelData.FormatDxt3));
        Assert.Equal(
            Veldrid.PixelFormat.BC3_UNorm,
            Ee2TexcacheTextureInstaller.MapPixelFormat(OpenSage.FileFormats.Nif.NifPixelData.FormatDxt5));
    }

    [Fact]
    public void UnknownFormatMappingThrows()
    {
        Assert.Throws<System.NotSupportedException>(
            () => Ee2TexcacheTextureInstaller.MapPixelFormat(0));
    }
}
