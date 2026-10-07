using System;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

[Collection("GraphicsTests")]
public sealed class ImportedTextureMaterializerTests
{
    [GraphicsFact]
    public void EquivalentEmbeddedSources_InOneScope_ReuseOneUpload()
    {
        using var graphics = new TestGraphicsContext();
        using var materializer = new ImportedTextureMaterializer(graphics.Context);

        var bytes = EncodePng();
        var first = ImportedImageSource.CreateEmbeddedBytes(bytes, "image/png", "a.glb#0");
        var second = ImportedImageSource.CreateEmbeddedBytes((byte[])bytes.Clone(), "image/png", "b.glb#1");

        var firstTexture = materializer.GetTexture(first);
        var secondTexture = materializer.GetTexture(second);

        Assert.Same(firstTexture, secondTexture);
        Assert.Equal(2u, firstTexture.Width);
        Assert.Equal(2u, firstTexture.Height);
    }

    [GraphicsFact]
    public void DifferentEmbeddedBytes_DoNotCollide()
    {
        using var graphics = new TestGraphicsContext();
        using var materializer = new ImportedTextureMaterializer(graphics.Context);

        var firstTexture = materializer.GetTexture(
            ImportedImageSource.CreateEmbeddedBytes(EncodePng(), "image/png", "a.glb#0"));
        var secondTexture = materializer.GetTexture(
            ImportedImageSource.CreateEmbeddedBytes(EncodePng(red: false), "image/png", "a.glb#1"));

        Assert.NotSame(firstTexture, secondTexture);
    }

    [GraphicsFact]
    public void EquivalentExternalPaths_ReuseOneUpload_AndDifferAcrossScopes()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/base.png", EncodePng());

        using var graphics = new TestGraphicsContext(fileSystem);
        using var materializer = new ImportedTextureMaterializer(graphics.Context);

        var source = ImportedImageSource.CreateVirtualPath("textures/base.png");
        Assert.Same(
            materializer.GetTexture(source),
            materializer.GetTexture(ImportedImageSource.CreateVirtualPath("textures/base.png")));

        // A different materializer owns a different scope: no cross-scope collision.
        using var otherMaterializer = new ImportedTextureMaterializer(graphics.Context);
        Assert.NotSame(materializer.GetTexture(source), otherMaterializer.GetTexture(source));
    }

    [GraphicsFact]
    public void MipIntent_IsPartOfTheTextureIdentity()
    {
        using var graphics = new TestGraphicsContext();
        using var materializer = new ImportedTextureMaterializer(graphics.Context);

        var source = ImportedImageSource.CreateEmbeddedBytes(EncodePng(), "image/png", "a.glb#0");

        var withoutMips = materializer.GetTexture(source, generateMipmaps: false);
        var withMips = materializer.GetTexture(source, generateMipmaps: true);
        var withMipsAgain = materializer.GetTexture(source, generateMipmaps: true);

        Assert.NotSame(withoutMips, withMips);
        Assert.Same(withMips, withMipsAgain);
        Assert.True(withMips.MipLevels > 1);
        Assert.Equal(1u, withoutMips.MipLevels);
    }

    [GraphicsFact]
    public void SamplerCache_DistinguishesEveryField()
    {
        using var graphics = new TestGraphicsContext();
        using var materializer = new ImportedTextureMaterializer(graphics.Context);

        var full = new ImportedSampler(
            ImportedAddressMode.Repeat,
            ImportedAddressMode.Repeat,
            ImportedFilterMode.Linear,
            ImportedFilterMode.Linear,
            ImportedFilterMode.Linear);
        Assert.Same(materializer.GetSampler(full), materializer.GetSampler(new ImportedSampler()));

        Assert.NotSame(
            materializer.GetSampler(full),
            materializer.GetSampler(new ImportedSampler(addressModeU: ImportedAddressMode.Clamp)));
        Assert.NotSame(
            materializer.GetSampler(full),
            materializer.GetSampler(new ImportedSampler(addressModeV: ImportedAddressMode.MirroredRepeat)));
        Assert.NotSame(
            materializer.GetSampler(full),
            materializer.GetSampler(new ImportedSampler(minFilter: ImportedFilterMode.Nearest)));
        Assert.NotSame(
            materializer.GetSampler(full),
            materializer.GetSampler(new ImportedSampler(magFilter: ImportedFilterMode.Nearest)));
        Assert.NotSame(
            materializer.GetSampler(full),
            materializer.GetSampler(new ImportedSampler(mipFilter: ImportedFilterMode.Nearest)));
    }

    [GraphicsFact]
    public void Disposal_ReleasesResources_AndRejectsFurtherUse()
    {
        using var graphics = new TestGraphicsContext();
        var materializer = new ImportedTextureMaterializer(graphics.Context);

        var texture = materializer.GetTexture(
            ImportedImageSource.CreateEmbeddedBytes(EncodePng(), "image/png", "a.glb#0"));
        var sampler = materializer.GetSampler(new ImportedSampler());

        materializer.Dispose();

        Assert.True(texture.IsDisposed);
        Assert.True(sampler.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            materializer.GetTexture(ImportedImageSource.CreateEmbeddedBytes(EncodePng(), "image/png", "a.glb#1")));
        Assert.Throws<ObjectDisposedException>(() =>
            materializer.GetSampler(new ImportedSampler()));
    }

    private static byte[] EncodePng(bool red = true)
    {
        using var image = new Image<Rgba32>(2, 2);
        image[0, 0] = red ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 255, 0, 255);
        image[1, 0] = new Rgba32(0, 255, 0, 255);
        image[0, 1] = new Rgba32(0, 0, 255, 255);
        image[1, 1] = new Rgba32(255, 255, 0, 255);

        using var stream = new System.IO.MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }
}
