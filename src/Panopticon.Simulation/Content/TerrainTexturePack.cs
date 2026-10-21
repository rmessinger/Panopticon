using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Panopticon.Data.Tga;
using Panopticon.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid.ImageSharp;

namespace Panopticon.Content;

public sealed class TerrainTexturePack(string root)
{
    public static string DefaultRoot => Environment.GetEnvironmentVariable("PANOPTICON_TERRAIN_TEXTURE_PACK")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panopticon", "DerivedTerrain");

    public bool TryLoad(FileSystemEntry source, int size, out ImageSharpTexture texture, out string reason)
    {
        texture = null;
        if (size < 1 || size > 4096)
        {
            reason = "Unsupported offline terrain array size.";
            return false;
        }

        var levels = new List<Image<Rgba32>>();
        try
        {
            var hash = SourceHash(source);
            var directory = PackDirectory(hash, size);
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                reason = "No matching offline texture pack.";
                return false;
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
            var expectedLevels = BitOperations.Log2((uint)size) + 1;
            if (manifest == null || manifest.Algorithm != PrefilteredTerrainMipBuilder.AlgorithmVersion
                || manifest.SourceSha256 != hash || manifest.Size != size || manifest.MipSha256 == null
                || manifest.MipSha256.Length != expectedLevels)
            {
                throw new InvalidDataException("Terrain pack metadata does not match the source and mip layout.");
            }

            for (var level = 0; level < expectedLevels; level++)
            {
                var bytes = File.ReadAllBytes(Path.Combine(directory, $"mip_{level:D2}.png"));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != manifest.MipSha256[level])
                {
                    throw new InvalidDataException("Terrain pack mip checksum mismatch.");
                }

                var image = Image.Load<Rgba32>(bytes);
                levels.Add(image);
                var mipSize = Math.Max(1, size >> level);
                if (image.Width != mipSize || image.Height != mipSize)
                {
                    throw new InvalidDataException("Terrain pack mip dimensions are invalid.");
                }
            }

            texture = new ImageSharpTexture(levels.ToArray(), srgb: true);
            levels.Clear();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidImageContentException or UnknownImageFormatException)
        {
            reason = ex.Message;
            return false;
        }
        finally
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }
        }
    }

    public bool Bake(FileSystemEntry source, int size)
    {
        var hash = SourceHash(source);
        var directory = PackDirectory(hash, size);
        if (Directory.Exists(directory))
        {
            if (!TryLoad(source, size, out var existing, out var reason))
            {
                throw new InvalidDataException($"Existing terrain pack is invalid and was preserved: {directory}: {reason}");
            }

            foreach (var image in existing.Images)
            {
                image.Dispose();
            }

            return false;
        }

        var tga = TgaFile.FromFileSystemEntry(source);
        using var sourceImage = Image.LoadPixelData<Rgba32>(TgaFile.ConvertPixelsToRgba8(tga, true), tga.Header.Width, tga.Header.Height);
        var levels = PrefilteredTerrainMipBuilder.Generate(sourceImage, size);
        var staging = directory + $".building-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            var hashes = new string[levels.Length];
            for (var level = 0; level < levels.Length; level++)
            {
                var path = Path.Combine(staging, $"mip_{level:D2}.png");
                levels[level].SaveAsPng(path);
                hashes[level] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }

            var manifest = new Manifest(PrefilteredTerrainMipBuilder.AlgorithmVersion, hash, size, hashes);
            File.WriteAllText(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(staging, directory);
            return true;
        }
        finally
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }
        }
    }

    private string PackDirectory(string hash, int size)
    {
        if (size < 1 || size > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        return Path.Combine(root, PrefilteredTerrainMipBuilder.AlgorithmVersion, hash, size.ToString(CultureInfo.InvariantCulture));
    }

    private static string SourceHash(FileSystemEntry source)
    {
        using var stream = source.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record Manifest(string Algorithm, string SourceSha256, int Size, string[] MipSha256);
}
