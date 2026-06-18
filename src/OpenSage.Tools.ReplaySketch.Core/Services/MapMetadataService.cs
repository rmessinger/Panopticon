using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using OpenSage.Data;
using OpenSage.Data.Map;
using OpenSage.Data.Tga;
using OpenSage.IO;
using OpenSage.Scripting;
using OpenSage.Terrain;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Loads map metadata (extents, player-start positions, height sampling) from the
/// game's file system without launching a full game instance.
/// </summary>
public sealed class MapMetadataService
{
    private const int PreferredPreviewMinDimension = 512;
    private const int GeneratedPreviewMaxDimension = 1024;

    private readonly HeightMap? _heightMap;
    private readonly Vector3[] _playerStarts;

    public Vector3 ExtentMin { get; }
    public Vector3 ExtentMax { get; }
    public Vector3 MapCenter => (ExtentMin + ExtentMax) * 0.5f;

    /// <summary>
    /// RGBA8 pixel data for the bird's-eye preview thumbnail, or <c>null</c> if unavailable.
    /// Pixels are stored top-to-bottom, left-to-right, 4 bytes per pixel (R, G, B, A).
    /// </summary>
    public byte[]? PreviewImagePixels { get; }

    /// <summary>Width of <see cref="PreviewImagePixels"/> in pixels.</summary>
    public int PreviewImageWidth { get; }

    /// <summary>Height of <see cref="PreviewImagePixels"/> in pixels.</summary>
    public int PreviewImageHeight { get; }

    private MapMetadataService(
        HeightMap? heightMap,
        Vector3 extentMin, Vector3 extentMax,
        Vector3[] playerStarts,
        byte[]? previewImagePixels = null,
        int previewImageWidth = 0,
        int previewImageHeight = 0)
    {
        _heightMap = heightMap;
        _playerStarts = playerStarts;
        ExtentMin = extentMin;
        ExtentMax = extentMax;
        PreviewImagePixels = previewImagePixels;
        PreviewImageWidth = previewImageWidth;
        PreviewImageHeight = previewImageHeight;
    }

    /// <summary>
    /// Creates a lightweight instance for unit tests — no game files required.
    /// <see cref="SampleHeight"/> returns 0 for all positions.
    /// </summary>
    public static MapMetadataService CreateForTesting(
        Vector3 extentMin, Vector3 extentMax, params Vector3[] playerStarts)
        => new(null, extentMin, extentMax, playerStarts);

    /// <summary>
    /// Loads the given map file from the installation's virtual file system.
    /// Also attempts to load the companion <c>.tga</c> preview thumbnail.
    /// </summary>
    /// <param name="installation">A valid Generals installation.</param>
    /// <param name="mapPath">
    /// Map path as stored in replay metadata, e.g.
    /// <c>"maps/alpine assault/alpine assault.map"</c>.
    /// </param>
    public static MapMetadataService Load(GameInstallation installation, string mapPath)
    {
        using var fileSystem = installation.CreateFileSystem();

        var entry = fileSystem.GetFile(mapPath)
            ?? throw new FileNotFoundException($"Map not found in game file system: {mapPath}");

        var mapFile = MapFile.FromFileSystemEntry(entry);

        // ------------------------------------------------------------------
        // Load preview thumbnail (.tga at same path as the .map file)
        // ------------------------------------------------------------------
        byte[]? previewPixels = null;
        int previewWidth = 0, previewHeight = 0;

        var tgaPath = Path.ChangeExtension(mapPath, ".tga");
        var tgaEntry = fileSystem.GetFile(tgaPath);
        if (tgaEntry != null)
        {
            try
            {
                var tga = TgaFile.FromFileSystemEntry(tgaEntry);
                previewPixels = TgaFile.ConvertPixelsToRgba8(tga, alphaToOpaque: true);
                previewWidth = tga.Header.Width;
                previewHeight = tga.Header.Height;
            }
            catch (InvalidDataException)
            {
                // Preview is best-effort; fall back to synthetic generation below.
            }
        }

        // ------------------------------------------------------------------
        // Build HeightMap wrapper (gives us bilinear-interpolated SampleHeight)
        // ------------------------------------------------------------------
        var heightMapData = mapFile.HeightMapData
            ?? throw new InvalidDataException("Map file contains no HeightMapData.");

        var heightMap = new HeightMap(heightMapData);

        // ------------------------------------------------------------------
        // Compute world-space extents from HeightMap
        // HeightMap.HorizontalScale = 10; border cells are outside the playable area.
        // ------------------------------------------------------------------
        var border = (int)heightMapData.BorderWidth;
        var playableW = (int)heightMapData.Width - 2 * border;
        var playableH = (int)heightMapData.Height - 2 * border;

        const float hs = HeightMap.HorizontalScale;
        var extentMin = new Vector3(0f, 0f, heightMapData.MinZ);
        var extentMax = new Vector3(playableW * hs, playableH * hs, heightMapData.MaxZ);

        if (previewPixels == null || Math.Min(previewWidth, previewHeight) < PreferredPreviewMinDimension)
        {
            var cachePath = GetPreviewCachePath(mapPath, entry.Length);
            var cached = LoadPreviewCache(cachePath);
            if (cached.HasValue)
            {
                previewPixels = cached.Value.pixels;
                previewWidth = cached.Value.width;
                previewHeight = cached.Value.height;
            }
            else
            {
                var generated = GenerateHeightShadedPreview(heightMapData, GeneratedPreviewMaxDimension);
                if (generated != null)
                {
                    previewPixels = generated.Value.pixels;
                    previewWidth = generated.Value.width;
                    previewHeight = generated.Value.height;
                    SavePreviewCache(cachePath, previewPixels, previewWidth, previewHeight);
                }
            }
        }

        // ------------------------------------------------------------------
        // Extract Player_N_Start waypoints from the map's ObjectsList
        // ------------------------------------------------------------------
        var waypoints = new WaypointCollection(
            mapFile.ObjectsList?.Objects
                .Where(o => o.TypeName == Waypoint.ObjectTypeName)
                .Select(o => new Waypoint(o))
                ?? [],
            []);

        var playerStarts = new Vector3[8];
        for (var i = 1; i <= 8; i++)
        {
            if (waypoints.TryGetPlayerStart(i, out var wp) && wp is not null)
            {
                playerStarts[i - 1] = wp.Position;
            }
        }

        return new MapMetadataService(heightMap, extentMin, extentMax, playerStarts,
            previewPixels, previewWidth, previewHeight);
    }

    /// <summary>
    /// Returns the world-space start position for the given 0-based slot index.
    /// Falls back to <see cref="MapCenter"/> if the waypoint is not present.
    /// </summary>
    public Vector3 PlayerStart(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _playerStarts.Length)
        {
            var pos = _playerStarts[slotIndex];
            if (pos != Vector3.Zero) return pos;
        }
        return MapCenter;
    }

    /// <summary>Bilinear height sample at world XY coordinates.</summary>
    public float SampleHeight(float x, float y) => _heightMap?.GetHeight(x, y) ?? 0f;

    // Cache directory: %LocalAppData%/OpenSAGE/ReplaySketch/previews
    private static readonly string s_previewCacheDir = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "OpenSAGE", "ReplaySketch", "previews");

    private static string GetPreviewCachePath(string mapPath, uint fileLength)
    {
        var pathHash = SHA1.HashData(Encoding.UTF8.GetBytes(mapPath.ToLowerInvariant()));
        var hashHex = Convert.ToHexString(pathHash)[..16];
        return Path.Combine(s_previewCacheDir, $"{hashHex}-{fileLength}.bin");
    }

    private static (byte[] pixels, int width, int height)? LoadPreviewCache(string cachePath)
    {
        if (!File.Exists(cachePath))
            return null;
        try
        {
            using var fs = File.OpenRead(cachePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new BinaryReader(gz);
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var pixels = reader.ReadBytes(width * height * 4);
            if (pixels.Length != width * height * 4)
                return null;
            return (pixels, width, height);
        }
        catch
        {
            return null;
        }
    }

    private static void SavePreviewCache(string cachePath, byte[] pixels, int width, int height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var fs = File.Create(cachePath);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            using var writer = new BinaryWriter(gz);
            writer.Write(width);
            writer.Write(height);
            writer.Write(pixels);
        }
        catch
        {
            // Cache writes are best-effort; never block startup on a write failure.
        }
    }

    private static (byte[] pixels, int width, int height)? GenerateHeightShadedPreview(HeightMapData data, int maxDimension)
    {
        var border = (int)data.BorderWidth;
        var playableW = (int)data.Width - 2 * border;
        var playableH = (int)data.Height - 2 * border;
        if (playableW <= 1 || playableH <= 1)
        {
            return null;
        }

        var longest = Math.Max(playableW, playableH);
        var scale = Math.Min(1f, maxDimension / (float)longest);
        var outW = Math.Max(1, (int)MathF.Round(playableW * scale));
        var outH = Math.Max(1, (int)MathF.Round(playableH * scale));
        var pixels = new byte[outW * outH * 4];

        var minZ = data.MinZ;
        var zRange = Math.Max(data.MaxZ - data.MinZ, 1f);

        var light = Vector3.Normalize(new Vector3(-0.45f, 0.7f, 0.55f));

        Parallel.For(0, outH, py =>
        {
            var ty = py / (float)Math.Max(1, outH - 1);
            var srcY = border + (int)MathF.Round((1f - ty) * (playableH - 1));

            for (var px = 0; px < outW; px++)
            {
                var tx = px / (float)Math.Max(1, outW - 1);
                var srcX = border + (int)MathF.Round(tx * (playableW - 1));

                var x0 = Math.Max(border, srcX - 1);
                var x1 = Math.Min(border + playableW - 1, srcX + 1);
                var y0 = Math.Max(border, srcY - 1);
                var y1 = Math.Min(border + playableH - 1, srcY + 1);

                var hCenter = data.Elevations[srcX, srcY] * data.VerticalScale;
                var hL = data.Elevations[x0, srcY] * data.VerticalScale;
                var hR = data.Elevations[x1, srcY] * data.VerticalScale;
                var hU = data.Elevations[srcX, y0] * data.VerticalScale;
                var hD = data.Elevations[srcX, y1] * data.VerticalScale;

                var gradX = hR - hL;
                var gradY = hD - hU;
                var normal = Vector3.Normalize(new Vector3(-gradX, -gradY, 2.4f));
                var shade = Math.Clamp(Vector3.Dot(normal, light), 0f, 1f);

                var hNorm = Math.Clamp((hCenter - minZ) / zRange, 0f, 1f);
                var baseR = 58f + 110f * hNorm;
                var baseG = 70f + 100f * hNorm;
                var baseB = 52f + 85f * hNorm;

                var lighting = 0.55f + 0.7f * shade;
                var i = (py * outW + px) * 4;
                pixels[i + 0] = (byte)Math.Clamp((int)(baseR * lighting), 0, 255);
                pixels[i + 1] = (byte)Math.Clamp((int)(baseG * lighting), 0, 255);
                pixels[i + 2] = (byte)Math.Clamp((int)(baseB * lighting), 0, 255);
                pixels[i + 3] = 255;
            }
        });

        return (pixels, outW, outH);
    }
}
