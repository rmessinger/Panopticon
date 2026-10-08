using System;
using System.IO;
using Panopticon.Data.Dds;
using Panopticon.Data.Tga;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Utilities.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Veldrid.ImageSharp;

namespace Panopticon.Content.Loaders;

internal sealed class OnDemandTextureLoader : IVariantAwareAssetLoader<TextureAsset>
{
    /// <summary>
    /// Role key for cutout variants: alpha-tested textures whose mips are
    /// rebuilt to preserve the cutout coverage fraction (WI-G5-02).
    /// </summary>
    public const string CutoutRole = "cutout";

    /// <summary>Mirrors FailsAlphaTest in Assets/Shaders/Common.h.</summary>
    internal const float CutoutCutoff = 0.37647f;

    private static readonly string[] PossibleFileExtensions = new[]
    {
        ".dds",
        ".tga",
        ".jpg"
    };

    private readonly bool _generateMipMaps;
    private readonly IPathResolver _pathResolver;

    public OnDemandTextureLoader(
        bool generateMipMaps,
        IPathResolver pathResolver)
    {
        _generateMipMaps = generateMipMaps;
        _pathResolver = pathResolver;
    }

    public TextureAsset Load(string name, AssetLoadContext context)
    {
        return Load(name, string.Empty, context);
    }

    public TextureAsset Load(string name, string role, AssetLoadContext context)
    {
        // Find it in the file system.
        FileSystemEntry entry = null;
        foreach (var path in _pathResolver.GetPaths(name, context.Language))
        {
            foreach (var possibleFileExtension in PossibleFileExtensions)
            {
                var possibleFilePath = Path.ChangeExtension(path, possibleFileExtension);
                entry = context.FileSystem.GetFile(possibleFilePath);
                if (entry != null)
                {
                    break;
                }
            }

            if (entry != null)
            {
                break;
            }
        }

        if (entry == null)
        {
            return null;
        }

        var texture = role == CutoutRole
            ? LoadCutoutImpl(entry, context.FileSystem, context.GraphicsDevice)
            : role.Length == 0
                ? LoadImpl(entry, context.FileSystem, context.GraphicsDevice)
                : throw new InvalidOperationException($"Unknown texture role \"{role}\".");
        texture.Name = entry.FilePath;
        return new TextureAsset(texture, name);
    }

    private Texture LoadImpl(FileSystemEntry entry, FileSystem fileSystem, GraphicsDevice graphicsDevice)
    {
        switch (Path.GetExtension(entry.FilePath).ToLowerInvariant())
        {
            case ".dds":
                if (!DdsFile.IsDdsFile(entry))
                {
                    goto case ".tga";
                }

                var ddsFile = DdsFile.FromFileSystemEntry(entry);
                return CreateTextureFromDds(ddsFile, graphicsDevice);

            case ".tga":
                var tgaFile = TgaFile.FromFileSystemEntry(entry);
                return CreateTextureFromTga(tgaFile, graphicsDevice);

            case ".jpg":
                return CreateTextureFromJpg(entry, fileSystem, graphicsDevice);

            default:
                throw new InvalidOperationException();
        }
    }

    private Texture CreateTextureFromDds(DdsFile ddsFile, GraphicsDevice graphicsDevice)
    {
        return graphicsDevice.CreateStaticTexture2D(
            ddsFile.Header.Width,
            ddsFile.Header.Height,
            1,
            ddsFile.MipMaps,
            ddsFile.PixelFormat,
            false);
    }

    private Texture CreateTextureFromTga(TgaFile tgaFile, GraphicsDevice graphicsDevice)
    {
        var rgbaData = TgaFile.ConvertPixelsToRgba8(tgaFile);

        using (var tgaImage = Image.LoadPixelData<Rgba32>(
            rgbaData,
            tgaFile.Header.Width,
            tgaFile.Header.Height))
        {
            var imageSharpTexture = new ImageSharpTexture(
                tgaImage,
                _generateMipMaps);

            return CreateFromImageSharpTexture(imageSharpTexture, graphicsDevice);
        }
    }

    private Texture CreateTextureFromJpg(FileSystemEntry entry, FileSystem fileSystem, GraphicsDevice graphicsDevice)
    {
        ImageSharpTexture colorTexture;
        using (var stream = entry.Open())
        {
            colorTexture = new ImageSharpTexture(stream);
        }

        // Sometimes there's a .png sidecar file which contains the alpha channel.
        using (var stream = entry.Open())
        {
            var pngFilePath = Path.ChangeExtension(entry.FilePath, ".png");
            var pngEntry = fileSystem.GetFile(pngFilePath);
            if (pngEntry != null)
            {
                using var pngStream = pngEntry.Open();

                var alphaTexture = new ImageSharpTexture(pngStream);

                if (!colorTexture.Images[0].DangerousTryGetSinglePixelMemory(out var colorPixelSpan))
                {
                    throw new InvalidOperationException("Unable to get image pixelspan.");
                }

                if (!alphaTexture.Images[0].DangerousTryGetSinglePixelMemory(out var alphaPixelSpan))
                {
                    throw new InvalidOperationException("Unable to get image pixelspan.");
                }

                for (var i = 0; i < colorPixelSpan.Length; i++)
                {
                    colorPixelSpan.Span[i].A = alphaPixelSpan.Span[i].A;
                }
            }
        }

        return CreateFromImageSharpTexture(colorTexture, graphicsDevice);
    }

    private Texture CreateFromImageSharpTexture(ImageSharpTexture imageSharpTexture, GraphicsDevice graphicsDevice)
    {
        return imageSharpTexture.CreateDeviceTexture(
            graphicsDevice,
            graphicsDevice.ResourceFactory);
    }

    /// <summary>
    /// Cutout variant: decode the base image to RGBA (authoring format's authored
    /// mips are discarded) and rebuild the chain with coverage-preserving alpha.
    /// The JPG path merges the PNG alpha sidecar into the base before chaining,
    /// which the regular path only does for mip 0.
    /// </summary>
    private Texture LoadCutoutImpl(FileSystemEntry entry, FileSystem fileSystem, GraphicsDevice graphicsDevice)
    {
        switch (Path.GetExtension(entry.FilePath).ToLowerInvariant())
        {
            case ".dds":
                if (!DdsFile.IsDdsFile(entry))
                {
                    goto case ".tga";
                }

                var ddsFile = DdsFile.FromFileSystemEntry(entry);
                using (var ddsBase = DdsBlockDecoder.DecodeBaseMip(ddsFile))
                {
                    return CreateCutoutTexture(ddsBase, graphicsDevice);
                }

            case ".tga":
                var tgaFile = TgaFile.FromFileSystemEntry(entry);
                var rgbaData = TgaFile.ConvertPixelsToRgba8(tgaFile);
                using (var tgaBase = Image.LoadPixelData<Rgba32>(
                    rgbaData,
                    tgaFile.Header.Width,
                    tgaFile.Header.Height))
                {
                    return CreateCutoutTexture(tgaBase, graphicsDevice);
                }

            case ".jpg":
                using (var jpgBase = LoadJpgBaseWithSidecarAlpha(entry, fileSystem))
                {
                    return CreateCutoutTexture(jpgBase, graphicsDevice);
                }

            default:
                throw new InvalidOperationException();
        }
    }

    private Image<Rgba32> LoadJpgBaseWithSidecarAlpha(FileSystemEntry entry, FileSystem fileSystem)
    {
        Image<Rgba32> colorImage;
        using (var stream = entry.Open())
        {
            colorImage = Image.Load<Rgba32>(stream);
        }

        var pngFilePath = Path.ChangeExtension(entry.FilePath, ".png");
        var pngEntry = fileSystem.GetFile(pngFilePath);
        if (pngEntry == null)
        {
            return colorImage;
        }

        using var alphaImage = Image.Load<Rgba32>(pngEntry.Open());
        if (!colorImage.DangerousTryGetSinglePixelMemory(out var colorSpan)
            || !alphaImage.DangerousTryGetSinglePixelMemory(out var alphaSpan))
        {
            throw new InvalidOperationException("Unable to get image pixelspan.");
        }

        for (var i = 0; i < colorSpan.Length; i++)
        {
            colorSpan.Span[i].A = alphaSpan.Span[i].A;
        }

        return colorImage;
    }

    private Texture CreateCutoutTexture(Image<Rgba32> baseImage, GraphicsDevice graphicsDevice)
    {
        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(baseImage, CutoutCutoff);
        var imageSharpTexture = new ImageSharpTexture(chain);
        return CreateFromImageSharpTexture(imageSharpTexture, graphicsDevice);
    }
}
