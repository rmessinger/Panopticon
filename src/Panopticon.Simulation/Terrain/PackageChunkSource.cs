#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;

namespace Panopticon.Terrain;

/// <summary>Serves chunks from an H3 map package directory, reading only the layer files each load needs.</summary>
public sealed class PackageChunkSource : ITerrainChunkSource
{
    private readonly string _packageRoot;

    private readonly MapPackageTerrainLayout _layout;

    private readonly Dictionary<(int ChunkX, int ChunkY, string LayerKind), MapPackageLayerEntry> _entries;

    public PackageChunkSource(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _packageRoot = packageRoot;

        var manifestPath = Path.Combine(packageRoot, MapPackageFormat.ManifestFileName);
        var manifestInfo = new FileInfo(manifestPath);

        if (!manifestInfo.Exists)
        {
            throw new TerrainChunkLoadException($"Package manifest '{manifestPath}' does not exist.");
        }

        if (manifestInfo.Length > MapPackageFormat.MaxManifestBytes)
        {
            throw new TerrainChunkLoadException(
                $"Package manifest '{manifestPath}' length {manifestInfo.Length} bytes exceeds the limit of {MapPackageFormat.MaxManifestBytes} bytes.");
        }

        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(manifestPath));

        if (manifest is null || diagnostics.Count > 0)
        {
            throw new TerrainChunkLoadException(
                $"Package manifest '{manifestPath}' is invalid: {string.Join("; ", diagnostics.Select(diagnostic => diagnostic.ToString()))}");
        }

        _layout = manifest.Layout;
        _entries = manifest.Layers.ToDictionary(entry => (entry.ChunkX, entry.ChunkY, entry.LayerKind));
    }

    public int ChunksX => _layout.ChunksX;

    public int ChunksY => _layout.ChunksY;

    public int LatticeWidth => _layout.LatticeWidth;

    public int LatticeHeight => _layout.LatticeHeight;

    public TerrainChunkData LoadChunk(ChunkKey key)
    {
        if (key.Cx < 0 || key.Cx >= ChunksX || key.Cy < 0 || key.Cy >= ChunksY)
        {
            throw new TerrainChunkLoadException(
                $"Chunk ({key.Cx}, {key.Cy}) lies outside the {ChunksX} x {ChunksY} chunk grid.");
        }

        var heightEntry = FindEntry(key, MapPackageFormat.LayerKindHeights)
            ?? throw new TerrainChunkLoadException($"Chunk ({key.Cx}, {key.Cy}) has no heights entry in the package manifest.");
        var heights = MapPackageChunkFileReader.ReadHeights(_packageRoot, heightEntry);

        if (!heights.Success)
        {
            throw Failure(key, heights.Diagnostics);
        }

        byte[]? materials = null;

        if (FindEntry(key, MapPackageFormat.LayerKindMaterials) is { } materialEntry)
        {
            var materialResult = MapPackageChunkFileReader.ReadMaterials(_packageRoot, materialEntry);

            if (!materialResult.Success)
            {
                throw Failure(key, materialResult.Diagnostics);
            }

            materials = materialResult.Payload;
        }

        byte[]? masks = null;

        if (FindEntry(key, MapPackageFormat.LayerKindMasks) is { } maskEntry)
        {
            var maskResult = MapPackageChunkFileReader.ReadMasks(_packageRoot, maskEntry);

            if (!maskResult.Success)
            {
                throw Failure(key, maskResult.Diagnostics);
            }

            masks = maskResult.Payload;
        }

        var chunk = new Chunk(
            key,
            new HeightLayer(heights.Payload),
            materials is null ? null : new MaterialLayer(materials),
            masks is null ? null : new MaskLayer(masks),
            null);
        var coreWidth = Math.Min(MapChunkGeometry.CorePointsPerAxis, _layout.LatticeWidth - (key.Cx * MapChunkGeometry.IntervalsPerChunkAxis));
        var coreHeight = Math.Min(MapChunkGeometry.CorePointsPerAxis, _layout.LatticeHeight - (key.Cy * MapChunkGeometry.IntervalsPerChunkAxis));

        try
        {
            return TerrainChunkData.FromChunk(chunk, coreWidth, coreHeight);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainChunkLoadException($"Chunk ({key.Cx}, {key.Cy}) failed validation: {ex.Message}", ex);
        }
    }

    private MapPackageLayerEntry? FindEntry(ChunkKey key, string layerKind) =>
        _entries.TryGetValue((key.Cx, key.Cy, layerKind), out var entry) ? entry : null;

    private static TerrainChunkLoadException Failure(ChunkKey key, IReadOnlyList<MapPackageDiagnostic> diagnostics) =>
        new($"Chunk ({key.Cx}, {key.Cy}) failed to load from the package: {string.Join("; ", diagnostics.Select(diagnostic => diagnostic.ToString()))}");
}
