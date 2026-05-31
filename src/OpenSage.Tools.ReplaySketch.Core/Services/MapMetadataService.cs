using System;
using System.IO;
using System.Linq;
using System.Numerics;
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
            catch
            {
                // Preview is best-effort; ignore any decoding errors.
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
}
