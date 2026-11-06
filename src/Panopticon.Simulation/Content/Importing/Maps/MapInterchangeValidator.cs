#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Model-level validation for <see cref="MapInterchange"/>, run before any runtime materialization.
/// A model is publishable only when <see cref="Validate"/> returns an empty list; a failed check
/// publishes no partial content.
/// </summary>
/// <remarks>
/// Stored height layout mirrors the Geospatial TerrainChunk contract: a row-major 131 x 131 array
/// whose row 0 is the southern apron, row 130 the northern apron, column 0 the western apron, and
/// column 130 the eastern apron. Core lattice point (i, j) with i and j in 0..128 maps to stored
/// row j + 1 and column i + 1, so rows run south to north and columns west to east. The shared core
/// edge point of horizontally adjacent chunks appears twice (core column 128 of the west chunk and
/// core column 0 of the east chunk) and must hold one value; the same holds vertically for core
/// row 128 of the south chunk and core row 0 of the north chunk. Where a neighbour exists, each
/// apron sample equals that neighbour's core sample at the same lattice point: east apron against
/// the east neighbour's core column 0, west apron against the west neighbour's core column 128,
/// north apron against the north neighbour's core row 0, south apron against the south neighbour's
/// core row 128.
/// </remarks>
public static class MapInterchangeValidator
{
    private const int StoredAxis = MapChunkGeometry.StoredPointsPerAxis;

    private const int CoreAxis = MapChunkGeometry.CorePointsPerAxis;

    private const int MaxPaletteEntries = 256;

    public static IReadOnlyList<MapInterchangeDiagnostic> Validate(MapInterchange map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(map.Layout);
        ArgumentNullException.ThrowIfNull(map.Blend);
        ArgumentNullException.ThrowIfNull(map.Provenance);
        ArgumentNullException.ThrowIfNull(map.RetainedSections);

        var diagnostics = new List<MapInterchangeDiagnostic>();
        ValidateSchemaVersion(map, diagnostics);
        var chunks = ValidateChunkInventory(map, diagnostics);
        ValidateLayers(chunks, diagnostics);
        ValidatePalette(map, chunks, diagnostics);
        ValidateImagery(chunks, diagnostics);
        ValidateStableIds(map, diagnostics);
        ValidateRetainedSections(map, diagnostics);
        ValidateSeamsAndAprons(chunks, diagnostics);
        return diagnostics;
    }

    private static void ValidateSchemaVersion(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (map.SchemaVersion == MapInterchange.CurrentSchemaVersion)
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.SchemaVersion,
            $"SchemaVersion {map.SchemaVersion} is not the current interchange schema version {MapInterchange.CurrentSchemaVersion}."));
    }

    private static Dictionary<ChunkKey, Chunk> ValidateChunkInventory(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        var chunks = new Dictionary<ChunkKey, Chunk>();
        var expected = new HashSet<ChunkKey>();

        if (map.Layout.ChunksX > 0 && map.Layout.ChunksY > 0)
        {
            for (var cx = 0; cx < map.Layout.ChunksX; cx++)
            {
                for (var cy = 0; cy < map.Layout.ChunksY; cy++)
                {
                    expected.Add(new ChunkKey(cx, cy));
                }
            }
        }

        foreach (var chunk in map.Layout.Chunks)
        {
            if (!expected.Contains(chunk.Key))
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ChunkInventory,
                    $"Chunk {Describe(chunk.Key)} lies outside the {map.Layout.ChunksX} x {map.Layout.ChunksY} chunk grid."));
                continue;
            }

            if (chunks.ContainsKey(chunk.Key))
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ChunkInventory,
                    $"Chunk {Describe(chunk.Key)} appears more than once."));
                continue;
            }

            chunks.Add(chunk.Key, chunk);
        }

        foreach (var key in expected)
        {
            if (chunks.ContainsKey(key))
            {
                continue;
            }

            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.ChunkInventory,
                $"Chunk {Describe(key)} of the {map.Layout.ChunksX} x {map.Layout.ChunksY} chunk grid is missing."));
        }

        return chunks;
    }

    private static void ValidateLayers(Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        var definedBits = (byte)(MapMaskFlags.Impassable | MapMaskFlags.Unbuildable);

        foreach (var chunk in chunks.Values)
        {
            var name = Describe(chunk.Key);

            if (chunk.Heights.Samples.Count != MapChunkGeometry.StoredHeightSamplesPerChunk)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightLength,
                    $"Chunk {name} stores {chunk.Heights.Samples.Count} height samples; expected {MapChunkGeometry.StoredHeightSamplesPerChunk} ({StoredAxis} x {StoredAxis})."));
            }
            else
            {
                for (var index = 0; index < chunk.Heights.Samples.Count; index++)
                {
                    if (float.IsFinite(chunk.Heights.Samples[index]))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.HeightFinite,
                        $"Chunk {name} height sample {index} is not a finite float32 value."));
                    break;
                }
            }

            if (chunk.Materials.Indices.Count != MapChunkGeometry.CoreCellsPerChunk)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaterialLength,
                    $"Chunk {name} stores {chunk.Materials.Indices.Count} material indices; expected {MapChunkGeometry.CoreCellsPerChunk} ({CoreAxis} x {CoreAxis})."));
            }

            if (chunk.Masks.Flags.Count != MapChunkGeometry.CoreCellsPerChunk)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaskLength,
                    $"Chunk {name} stores {chunk.Masks.Flags.Count} mask bytes; expected {MapChunkGeometry.CoreCellsPerChunk} ({CoreAxis} x {CoreAxis})."));
                continue;
            }

            for (var index = 0; index < chunk.Masks.Flags.Count; index++)
            {
                if ((chunk.Masks.Flags[index] & ~definedBits) == 0)
                {
                    continue;
                }

                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaskBits,
                    $"Chunk {name} mask cell {index} carries bits outside MapMaskFlags (0x{chunk.Masks.Flags[index]:X2})."));
                break;
            }
        }
    }

    private static void ValidatePalette(MapInterchange map, Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        var palette = map.Blend.Palette;

        if (palette.Count > MaxPaletteEntries)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.PaletteCount,
                $"Palette holds {palette.Count} entries; at most {MaxPaletteEntries} are allowed."));
            return;
        }

        var seen = new HashSet<byte>();

        foreach (var entry in palette)
        {
            if (seen.Add(entry.Index) && entry.Index < palette.Count)
            {
                continue;
            }

            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.PaletteDuplicateIndex,
                $"Palette entry '{entry.Name}' declares index {entry.Index}; entry indices must be unique and below the entry count {palette.Count}."));
            return;
        }

        foreach (var chunk in chunks.Values)
        {
            for (var index = 0; index < chunk.Materials.Indices.Count; index++)
            {
                if (chunk.Materials.Indices[index] < palette.Count)
                {
                    continue;
                }

                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.PaletteIndex,
                    $"Chunk {Describe(chunk.Key)} material cell {index} uses palette index {chunk.Materials.Indices[index]} outside the {palette.Count}-entry palette."));
                break;
            }
        }
    }

    private static void ValidateImagery(Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        var total = 0;
        var withImagery = 0;
        int? width = null;
        int? height = null;
        string? firstChunk = null;

        foreach (var chunk in chunks.Values)
        {
            total++;

            if (chunk.Imagery is not { } imagery)
            {
                continue;
            }

            withImagery++;

            if (imagery.Width < 0 || imagery.Height < 0 || (long)imagery.Width * imagery.Height * 4 != imagery.Rgba.Count)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ImageryConsistency,
                    $"Chunk {Describe(chunk.Key)} imagery declares {imagery.Width} x {imagery.Height} pixels but stores {imagery.Rgba.Count} RGBA bytes."));
                continue;
            }

            if (width.HasValue && (imagery.Width != width || imagery.Height != height))
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ImageryConsistency,
                    $"Chunk {Describe(chunk.Key)} imagery is {imagery.Width} x {imagery.Height} pixels; chunk {firstChunk} declared {width} x {height}."));
                continue;
            }

            if (!width.HasValue)
            {
                width = imagery.Width;
                height = imagery.Height;
                firstChunk = Describe(chunk.Key);
            }
        }

        if (withImagery > 0 && withImagery < total)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.ImageryConsistency,
                $"{withImagery} of {total} chunks carry imagery; either every chunk carries imagery or none does."));
        }
    }

    private static void ValidateStableIds(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>();

        if (map.Objects is { } objects)
        {
            foreach (var placed in objects.Objects)
            {
                RequireUniqueStableId(seen, placed.StableId, diagnostics);
            }
        }

        if (map.Waypoints is { } waypoints)
        {
            foreach (var waypoint in waypoints.Waypoints)
            {
                RequireUniqueStableId(seen, waypoint.StableId, diagnostics);
            }
        }

        foreach (var feature in map.Provenance.GeneratedFeatures)
        {
            RequireUniqueStableId(seen, feature.StableId, diagnostics);
        }
    }

    private static void RequireUniqueStableId(HashSet<string> seen, string stableId, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (seen.Add(stableId))
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.StableIdDuplicate,
            $"StableId '{stableId}' is used by more than one placed object, waypoint, or generated feature."));
    }

    private static void ValidateRetainedSections(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        foreach (var section in map.RetainedSections)
        {
            if (!MapSectionNames.IsKnownSection(section.SectionName))
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.RetainedName,
                    $"Retained section '{section.SectionName}' is not a known legacy section name."));
            }

            if (section.SectionVersion < 0)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.RetainedVersion,
                    $"Retained section '{section.SectionName}' carries version {section.SectionVersion}; section versions are non-negative."));
            }

            if (section.Bytes.IsEmpty)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.RetainedDigest,
                    $"Retained section '{section.SectionName}' stores no bytes."));
                continue;
            }

            var digest = Convert.ToHexString(SHA256.HashData(section.Bytes.Span));

            if (string.Equals(section.Sha256, digest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.RetainedDigest,
                $"Retained section '{section.SectionName}' digest does not match its bytes; declared '{section.Sha256}', computed '{digest}'."));
        }
    }

    private static void ValidateSeamsAndAprons(Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        foreach (var (key, chunk) in chunks)
        {
            if (!HasStoredHeights(chunk))
            {
                continue;
            }

            var name = Describe(key);

            if (chunks.TryGetValue(new ChunkKey(key.Cx + 1, key.Cy), out var east) && HasStoredHeights(east))
            {
                for (var j = 0; j < CoreAxis; j++)
                {
                    if (CoreSample(chunk, CoreAxis - 1, j) == CoreSample(east, 0, j))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.SeamEquality,
                        $"Chunk {name} core column {CoreAxis - 1} row {j} differs from east neighbour {Describe(east.Key)} core column 0 at the shared seam."));
                    break;
                }

                for (var j = 0; j < CoreAxis; j++)
                {
                    if (StoredSample(chunk, j + 1, StoredAxis - 1) == CoreSample(east, 0, j))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} east apron row {j} differs from east neighbour {Describe(east.Key)} core column 0 at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx - 1, key.Cy), out var west) && HasStoredHeights(west))
            {
                for (var j = 0; j < CoreAxis; j++)
                {
                    if (StoredSample(chunk, j + 1, 0) == CoreSample(west, CoreAxis - 1, j))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} west apron row {j} differs from west neighbour {Describe(west.Key)} core column {CoreAxis - 1} at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx, key.Cy + 1), out var north) && HasStoredHeights(north))
            {
                for (var i = 0; i < CoreAxis; i++)
                {
                    if (CoreSample(chunk, i, CoreAxis - 1) == CoreSample(north, i, 0))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.SeamEquality,
                        $"Chunk {name} core row {CoreAxis - 1} column {i} differs from north neighbour {Describe(north.Key)} core row 0 at the shared seam."));
                    break;
                }

                for (var i = 0; i < CoreAxis; i++)
                {
                    if (StoredSample(chunk, StoredAxis - 1, i + 1) == CoreSample(north, i, 0))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} north apron column {i} differs from north neighbour {Describe(north.Key)} core row 0 at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx, key.Cy - 1), out var south) && HasStoredHeights(south))
            {
                for (var i = 0; i < CoreAxis; i++)
                {
                    if (StoredSample(chunk, 0, i + 1) == CoreSample(south, i, CoreAxis - 1))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} south apron column {i} differs from south neighbour {Describe(south.Key)} core row {CoreAxis - 1} at the same lattice point."));
                    break;
                }
            }
        }
    }

    private static bool HasStoredHeights(Chunk chunk) => chunk.Heights.Samples.Count == MapChunkGeometry.StoredHeightSamplesPerChunk;

    private static float CoreSample(Chunk chunk, int i, int j) => chunk.Heights.Samples[((j + 1) * StoredAxis) + i + 1];

    private static float StoredSample(Chunk chunk, int row, int col) => chunk.Heights.Samples[(row * StoredAxis) + col];

    private static string Describe(ChunkKey key) => $"{key.Cx}_{key.Cy}";
}
