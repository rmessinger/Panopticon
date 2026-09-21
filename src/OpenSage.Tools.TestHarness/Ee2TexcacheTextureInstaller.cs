#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Nif;
using OpenSage.FileFormats.Nif;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Utilities;
using OpenSage.Utilities.Extensions;
using Veldrid;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Decodes EE2 texcache containers (DXT mips preserved block-for-block) and
/// registers them as runtime textures under their source .tga names, so
/// fixed-function materialization resolves the C4a models' Texture0
/// references. Requires the texture entry to resolve through the import file
/// system; never transcodes pixels.
/// </summary>
internal static class Ee2TexcacheTextureInstaller
{
    public static int Install(Game game, FileSystem fileSystem, IEnumerable<string> textureNames)
    {
        var installed = 0;
        foreach (var textureName in textureNames)
        {
            if (game.AssetStore.Textures.GetByName(textureName) is not null)
            {
                continue;
            }

            if (!NifMaterialConverter.TryResolveTexcache(textureName, new AssetImportContext(fileSystem, null), out var texcachePath))
            {
                continue;
            }

            var entry = fileSystem.GetFile(texcachePath)
                ?? throw new InvalidOperationException($"Texcache entry disappeared: {texcachePath}");

            // Zip entry streams are not seekable; buffer before parsing.
            using var buffered = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(buffered);
            }
            buffered.Position = 0;

            var file = NifFile.Read(buffered);
            if (file.Blocks.Count != 1 || file.Blocks[0] is not NifPixelData pixelData)
            {
                throw new InvalidDataException(
                    $"Texcache '{texcachePath}' does not hold a single NiPixelData block.");
            }

            var texture = CreateTexture(game.GraphicsDevice, pixelData, texcachePath);
            game.AssetStore.Textures.Add(new TextureAsset(texture, textureName));
            installed++;
        }

        return installed;
    }

    public static Texture CreateTexture(GraphicsDevice graphicsDevice, NifPixelData pixelData, string debugName)
    {
        var mips = new TextureMipMapData[pixelData.Mips.Count];
        for (var i = 0; i < mips.Length; i++)
        {
            var mip = pixelData.Mips[i];
            var size = pixelData.GetMipSize(i);
            var data = new byte[size];
            Array.Copy(pixelData.PixelData, (long)mip.Offset, data, 0, size);
            var bytesPerRow = pixelData.BytesPerBlock * Math.Max(1u, (mip.Width + 3) / 4);
            mips[i] = new TextureMipMapData(
                data,
                bytesPerRow,
                (uint)size,
                mip.Width,
                mip.Height);
        }

        var texture = graphicsDevice.CreateStaticTexture2D(
            mips[0].Width,
            mips[0].Height,
            1,
            mips,
            MapPixelFormat(pixelData.Format),
            isCubemap: false);
        texture.Name = debugName;
        return texture;
    }

    public static PixelFormat MapPixelFormat(uint nifFormat)
    {
        return nifFormat switch
        {
            NifPixelData.FormatDxt1 => PixelFormat.BC1_Rgba_UNorm,
            NifPixelData.FormatDxt3 => PixelFormat.BC2_UNorm,
            NifPixelData.FormatDxt5 => PixelFormat.BC3_UNorm,
            _ => throw new NotSupportedException($"NIF pixel format {nifFormat} has no BC mapping."),
        };
    }
}
