#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Model-level validation for <see cref="MapInterchange"/>, run before any runtime materialization.
/// A model is publishable only when <see cref="Validate"/> returns an empty list; a failed check
/// publishes no partial content. Validation never throws for content: malformed or null members
/// produce diagnostics and skip their dependent checks.
/// </summary>
/// <remarks>
/// Schema boundary: the interchange schema version is validated here. Source payload schema validity
/// and retained-section version semantic validity are reader and adapter responsibilities (WI-H2-03);
/// this contract defines no legacy version table. Retained sections are checked for a known name, a
/// non-negative version, non-empty bytes, and a matching SHA-256 digest.
/// Chunk (cx, cy) covers whole-map lattice points from cx * 128 horizontally and cy * 128 vertically.
/// Its core spans coreW = min(129, LatticeWidth - cx * 128) columns and coreH = min(129, LatticeHeight -
/// cy * 128) rows. Adjacent chunks share one edge lattice point. The stored height array is row-major
/// with (coreW + 2) columns and (coreH + 2) rows: row 0 is the southern apron, the last row the
/// northern apron, column 0 the western apron, and the last column the eastern apron. Core lattice
/// point (i, j) maps to stored row j + 1 and column i + 1, so rows run south to north and columns west
/// to east. Material and mask arrays hold coreW x coreH values. The shared core edge point of
/// horizontally adjacent chunks appears twice (core column coreW - 1 of the west chunk and core column
/// 0 of the east chunk) and must hold one value; the same holds vertically. Where a neighbour exists,
/// each apron sample equals the neighbouring chunk's core sample at the same lattice point. The east
/// apron lies on lattice column (cx + 1) * 128 + 1, core column 1 of the east neighbour; the west apron
/// on lattice column cx * 128 - 1, core column 127 of the west neighbour; the north and south aprons
/// follow by lattice row (north core row 1, south core row 127). Map-edge aprons follow the adapter's
/// clamp policy and are not validated. Seam and apron checks run only between chunks that are lattice
/// neighbours.
/// </remarks>
public static class MapInterchangeValidator
{
    private const int IntervalsPerChunkAxis = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int MaxPaletteEntries = 256;

    public static IReadOnlyList<MapInterchangeDiagnostic> Validate(MapInterchange map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var diagnostics = new List<MapInterchangeDiagnostic>();
        ValidateSchemaVersion(map, diagnostics);
        RequireComponent(map.Identity, nameof(MapInterchange.Identity), diagnostics);
        RequireComponent(map.Layout, nameof(MapInterchange.Layout), diagnostics);
        RequireComponent(map.Blend, nameof(MapInterchange.Blend), diagnostics);
        RequireComponent(map.Provenance, nameof(MapInterchange.Provenance), diagnostics);
        RequireComponent(map.Report, nameof(MapInterchange.Report), diagnostics);
        RequireComponent(map.RetainedSections, nameof(MapInterchange.RetainedSections), diagnostics);

        ValidateLatticeDimensions(map.Layout, diagnostics);
        TerrainLayout? sized = map.Layout is { } layout && LatticeIsPositive(layout) ? layout : null;
        var chunks = ValidateChunkInventory(map, diagnostics);
        ValidateLayers(sized, chunks, diagnostics);
        ValidatePalette(map, chunks, diagnostics);
        ValidateImagery(chunks, diagnostics);
        ValidateSeamsAndAprons(sized, chunks, diagnostics);
        ValidateLayoutQuantities(map, diagnostics);
        var stableIds = new HashSet<string>();
        ValidatePlacements(map, stableIds, diagnostics);
        ValidateWaypoints(map, stableIds, diagnostics);
        ValidateProvenance(map, stableIds, diagnostics);
        ValidateRetainedSections(map, diagnostics);
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

    private static void RequireComponent(object? component, string name, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (component is not null)
        {
            return;
        }

        AddNullComponent(diagnostics, name);
    }

    private static void AddNullComponent(List<MapInterchangeDiagnostic> diagnostics, string name)
    {
        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.NullComponent,
            $"Required component '{name}' is null; its dependent checks are skipped."));
    }

    private static void ValidateLatticeDimensions(TerrainLayout? layout, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (layout is null)
        {
            return;
        }

        if (!LatticeIsPositive(layout))
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.LatticeDimensions,
                $"TerrainLayout lattice is {layout.LatticeWidth} x {layout.LatticeHeight} points; both must be positive."));
            return;
        }

        var expectedChunksX = ChunksForLattice(layout.LatticeWidth);
        var expectedChunksY = ChunksForLattice(layout.LatticeHeight);

        if (layout.ChunksX == expectedChunksX && layout.ChunksY == expectedChunksY)
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.LatticeDimensions,
            $"Chunk grid {layout.ChunksX} x {layout.ChunksY} does not tile the {layout.LatticeWidth} x {layout.LatticeHeight} lattice; the grid requires {expectedChunksX} x {expectedChunksY}."));
    }

    private static bool LatticeIsPositive(TerrainLayout layout) => layout.LatticeWidth > 0 && layout.LatticeHeight > 0;

    private static long ChunksForLattice(int latticePoints) => (latticePoints - 1L + IntervalsPerChunkAxis - 1) / IntervalsPerChunkAxis;

    private static Dictionary<ChunkKey, Chunk> ValidateChunkInventory(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        var chunks = new Dictionary<ChunkKey, Chunk>();
        var layout = map.Layout;

        if (layout is null)
        {
            return chunks;
        }

        var chunkList = layout.Chunks;

        if (chunkList is null)
        {
            AddNullComponent(diagnostics, "TerrainLayout.Chunks");
            return chunks;
        }

        var gridDimensionsValid = layout.ChunksX > 0 && layout.ChunksY > 0;

        foreach (var chunk in chunkList)
        {
            if (chunk is null)
            {
                AddNullComponent(diagnostics, "TerrainLayout.Chunks entry");
                continue;
            }

            if (!gridDimensionsValid || chunk.Key.Cx < 0 || chunk.Key.Cx >= layout.ChunksX || chunk.Key.Cy < 0 || chunk.Key.Cy >= layout.ChunksY)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ChunkInventory,
                    $"Chunk {Describe(chunk.Key)} lies outside the {layout.ChunksX} x {layout.ChunksY} chunk grid."));
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

        var declaredCount = (long)layout.ChunksX * layout.ChunksY;

        if (!gridDimensionsValid || declaredCount != chunkList.Count)
        {
            // Per-cell enumeration is skipped: the declared count may be huge or negative.
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.ChunkInventory,
                $"Chunk grid declares {layout.ChunksX} x {layout.ChunksY} = {declaredCount} chunks; the model carries {chunkList.Count}."));
            return chunks;
        }

        // Counts match, so this enumeration is bounded by the actual chunk list length.
        for (var cx = 0; cx < layout.ChunksX; cx++)
        {
            for (var cy = 0; cy < layout.ChunksY; cy++)
            {
                var key = new ChunkKey(cx, cy);

                if (chunks.ContainsKey(key))
                {
                    continue;
                }

                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.ChunkInventory,
                    $"Chunk {Describe(key)} of the {layout.ChunksX} x {layout.ChunksY} chunk grid is missing."));
            }
        }

        return chunks;
    }

    private static void ValidateLayers(TerrainLayout? sized, Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        var definedBits = (byte)(MapMaskFlags.Impassable | MapMaskFlags.Unbuildable);

        foreach (var chunk in chunks.Values)
        {
            var name = Describe(chunk.Key);
            ChunkExtent? extent = sized is null ? null : ChunkExtent.Of(sized, chunk.Key);
            var heights = chunk.Heights;

            if (heights is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Heights");
            }
            else if (heights.Samples is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Heights.Samples");
            }
            else if (extent is { } heightExtent && heights.Samples.Count != heightExtent.StoredSampleCount)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.HeightLength,
                    $"Chunk {name} stores {heights.Samples.Count} height samples; expected {heightExtent.StoredSampleCount} ({heightExtent.StoredWidth} x {heightExtent.StoredHeight})."));
            }
            else
            {
                for (var index = 0; index < heights.Samples.Count; index++)
                {
                    if (float.IsFinite(heights.Samples[index]))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.HeightFinite,
                        $"Chunk {name} height sample {index} is not a finite float32 value."));
                    break;
                }
            }

            var materials = chunk.Materials;

            if (materials is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Materials");
            }
            else if (materials.Indices is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Materials.Indices");
            }
            else if (extent is { } materialExtent && materials.Indices.Count != materialExtent.CoreCellCount)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaterialLength,
                    $"Chunk {name} stores {materials.Indices.Count} material indices; expected {materialExtent.CoreCellCount} ({materialExtent.CoreWidth} x {materialExtent.CoreHeight})."));
            }

            var masks = chunk.Masks;

            if (masks is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Masks");
                continue;
            }

            if (masks.Flags is null)
            {
                AddNullComponent(diagnostics, $"Chunk {name} Masks.Flags");
                continue;
            }

            if (extent is { } maskExtent && masks.Flags.Count != maskExtent.CoreCellCount)
            {
                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaskLength,
                    $"Chunk {name} stores {masks.Flags.Count} mask bytes; expected {maskExtent.CoreCellCount} ({maskExtent.CoreWidth} x {maskExtent.CoreHeight})."));
                continue;
            }

            for (var index = 0; index < masks.Flags.Count; index++)
            {
                if ((masks.Flags[index] & ~definedBits) == 0)
                {
                    continue;
                }

                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.MaskBits,
                    $"Chunk {name} mask cell {index} carries bits outside MapMaskFlags (0x{masks.Flags[index]:X2})."));
                break;
            }
        }
    }

    private static void ValidatePalette(MapInterchange map, Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        var blend = map.Blend;

        if (blend is null)
        {
            return;
        }

        var palette = blend.Palette;

        if (palette is null)
        {
            AddNullComponent(diagnostics, "BlendModel.Palette");
            return;
        }

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
            if (entry is null)
            {
                AddNullComponent(diagnostics, "BlendModel.Palette entry");
                return;
            }

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
            var indices = chunk.Materials?.Indices;

            if (indices is null)
            {
                continue;
            }

            for (var index = 0; index < indices.Count; index++)
            {
                if (indices[index] < palette.Count)
                {
                    continue;
                }

                diagnostics.Add(new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.PaletteIndex,
                    $"Chunk {Describe(chunk.Key)} material cell {index} uses palette index {indices[index]} outside the {palette.Count}-entry palette."));
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

            if (imagery.Rgba is null)
            {
                AddNullComponent(diagnostics, $"Chunk {Describe(chunk.Key)} Imagery.Rgba");
                continue;
            }

            var pixels = (long)imagery.Width * imagery.Height;

            if (imagery.Width <= 0 || imagery.Height <= 0 || imagery.Rgba.Count % 4 != 0 || pixels != imagery.Rgba.Count / 4)
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

    private static void ValidateSeamsAndAprons(TerrainLayout? sized, Dictionary<ChunkKey, Chunk> chunks, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (sized is null)
        {
            return;
        }

        foreach (var (key, chunk) in chunks)
        {
            var extent = ChunkExtent.Of(sized, key);

            if (!HasStoredHeights(chunk, extent))
            {
                continue;
            }

            var name = Describe(key);

            if (chunks.TryGetValue(new ChunkKey(key.Cx + 1, key.Cy), out var east) && HasStoredHeights(east, ChunkExtent.Of(sized, east.Key)))
            {
                var eastExtent = ChunkExtent.Of(sized, east.Key);
                var seamX = extent.OriginX + extent.CoreWidth - 1;
                var apronX = seamX + 1;
                var rows = Math.Min(extent.CoreHeight, eastExtent.CoreHeight);

                for (var j = 0; j < rows; j++)
                {
                    var y = extent.OriginY + j;

                    if (!Conflicts(chunk, extent, east, eastExtent, seamX, y))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.SeamEquality,
                        $"Chunk {name} core column {extent.CoreWidth - 1} row {j} differs from east neighbour {Describe(east.Key)} core column {seamX - eastExtent.OriginX} at the shared seam."));
                    break;
                }

                for (var j = 0; j < rows; j++)
                {
                    var y = extent.OriginY + j;

                    if (!Conflicts(chunk, extent, east, eastExtent, apronX, y))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} east apron row {j} differs from east neighbour {Describe(east.Key)} core column {apronX - eastExtent.OriginX} at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx - 1, key.Cy), out var west) && HasStoredHeights(west, ChunkExtent.Of(sized, west.Key)))
            {
                var westExtent = ChunkExtent.Of(sized, west.Key);
                var apronX = extent.OriginX - 1;
                var rows = Math.Min(extent.CoreHeight, westExtent.CoreHeight);

                for (var j = 0; j < rows; j++)
                {
                    var y = extent.OriginY + j;

                    if (!Conflicts(chunk, extent, west, westExtent, apronX, y))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} west apron row {j} differs from west neighbour {Describe(west.Key)} core column {apronX - westExtent.OriginX} at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx, key.Cy + 1), out var north) && HasStoredHeights(north, ChunkExtent.Of(sized, north.Key)))
            {
                var northExtent = ChunkExtent.Of(sized, north.Key);
                var seamY = extent.OriginY + extent.CoreHeight - 1;
                var apronY = seamY + 1;
                var columns = Math.Min(extent.CoreWidth, northExtent.CoreWidth);

                for (var i = 0; i < columns; i++)
                {
                    var x = extent.OriginX + i;

                    if (!Conflicts(chunk, extent, north, northExtent, x, seamY))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.SeamEquality,
                        $"Chunk {name} core row {extent.CoreHeight - 1} column {i} differs from north neighbour {Describe(north.Key)} core row {seamY - northExtent.OriginY} at the shared seam."));
                    break;
                }

                for (var i = 0; i < columns; i++)
                {
                    var x = extent.OriginX + i;

                    if (!Conflicts(chunk, extent, north, northExtent, x, apronY))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} north apron column {i} differs from north neighbour {Describe(north.Key)} core row {apronY - northExtent.OriginY} at the same lattice point."));
                    break;
                }
            }

            if (chunks.TryGetValue(new ChunkKey(key.Cx, key.Cy - 1), out var south) && HasStoredHeights(south, ChunkExtent.Of(sized, south.Key)))
            {
                var southExtent = ChunkExtent.Of(sized, south.Key);
                var apronY = extent.OriginY - 1;
                var columns = Math.Min(extent.CoreWidth, southExtent.CoreWidth);

                for (var i = 0; i < columns; i++)
                {
                    var x = extent.OriginX + i;

                    if (!Conflicts(chunk, extent, south, southExtent, x, apronY))
                    {
                        continue;
                    }

                    diagnostics.Add(new MapInterchangeDiagnostic(
                        MapInterchangeDiagnostic.ApronAgreement,
                        $"Chunk {name} south apron column {i} differs from south neighbour {Describe(south.Key)} core row {apronY - southExtent.OriginY} at the same lattice point."));
                    break;
                }
            }
        }
    }

    private static void ValidateLayoutQuantities(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        var layout = map.Layout;

        if (layout is null)
        {
            return;
        }

        RequireFinitePositiveQuantity(layout.SampleSpacing, "TerrainLayout.SampleSpacing", diagnostics);
        RequireUnitFrame(layout.SampleSpacing, "TerrainLayout.SampleSpacing", diagnostics);

        if (layout.SampleSpacing.Frame != MapQuantityFrame.MapLocalXy)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.CoordinateFrame,
                $"TerrainLayout.SampleSpacing is declared in frame {layout.SampleSpacing.Frame}; it must be {nameof(MapQuantityFrame.MapLocalXy)}."));
        }

        var transform = layout.GeospatialTransform;

        if (transform is null)
        {
            AddNullComponent(diagnostics, "TerrainLayout.GeospatialTransform");
            return;
        }

        if (!double.IsFinite(transform.MetersPerGameUnit) || transform.MetersPerGameUnit <= 0)
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.QuantityFinite,
                $"GeospatialTransform.MetersPerGameUnit is {transform.MetersPerGameUnit}; it must be finite and greater than zero."));
        }

        if (transform.VerticalExaggeration is { } exaggeration && !double.IsFinite(exaggeration))
        {
            diagnostics.Add(new MapInterchangeDiagnostic(
                MapInterchangeDiagnostic.QuantityFinite,
                $"GeospatialTransform.VerticalExaggeration is {exaggeration}; it must be finite when present."));
        }

        if (transform.OriginEasting is { } originEasting)
        {
            RequireQuantity(originEasting, "GeospatialTransform.OriginEasting", diagnostics);
        }

        if (transform.OriginNorthing is { } originNorthing)
        {
            RequireQuantity(originNorthing, "GeospatialTransform.OriginNorthing", diagnostics);
        }

        if (transform.VerticalOrigin is { } verticalOrigin)
        {
            RequireQuantity(verticalOrigin, "GeospatialTransform.VerticalOrigin", diagnostics);
        }
    }

    private static void ValidatePlacements(MapInterchange map, HashSet<string> stableIds, List<MapInterchangeDiagnostic> diagnostics)
    {
        var objects = map.Objects;

        if (objects is null)
        {
            return;
        }

        if (objects.Objects is null)
        {
            AddNullComponent(diagnostics, "PlacedObjectSet.Objects");
            return;
        }

        foreach (var placed in objects.Objects)
        {
            if (placed is null)
            {
                AddNullComponent(diagnostics, "PlacedObjectSet.Objects entry");
                continue;
            }

            RequireUniqueStableId(stableIds, placed.StableId, diagnostics);
            RequireQuantity(placed.X, $"PlacedObject '{placed.StableId}' X", diagnostics);
            RequireQuantity(placed.Y, $"PlacedObject '{placed.StableId}' Y", diagnostics);
            RequireQuantity(placed.Z, $"PlacedObject '{placed.StableId}' Z", diagnostics);
            RequireHorizontalFrame(placed.X.Frame, placed.Y.Frame, $"PlacedObject '{placed.StableId}'", diagnostics);
            RequireVerticalFrame(placed.Z.Frame, $"PlacedObject '{placed.StableId}'", diagnostics);
        }
    }

    private static void ValidateWaypoints(MapInterchange map, HashSet<string> stableIds, List<MapInterchangeDiagnostic> diagnostics)
    {
        var waypoints = map.Waypoints;

        if (waypoints is null)
        {
            return;
        }

        if (waypoints.Waypoints is null)
        {
            AddNullComponent(diagnostics, "WaypointSet.Waypoints");
            return;
        }

        foreach (var waypoint in waypoints.Waypoints)
        {
            if (waypoint is null)
            {
                AddNullComponent(diagnostics, "WaypointSet.Waypoints entry");
                continue;
            }

            RequireUniqueStableId(stableIds, waypoint.StableId, diagnostics);
            RequireQuantity(waypoint.X, $"Waypoint '{waypoint.StableId}' X", diagnostics);
            RequireQuantity(waypoint.Y, $"Waypoint '{waypoint.StableId}' Y", diagnostics);
            RequireQuantity(waypoint.Z, $"Waypoint '{waypoint.StableId}' Z", diagnostics);
            RequireHorizontalFrame(waypoint.X.Frame, waypoint.Y.Frame, $"Waypoint '{waypoint.StableId}'", diagnostics);
            RequireVerticalFrame(waypoint.Z.Frame, $"Waypoint '{waypoint.StableId}'", diagnostics);
        }
    }

    private static void RequireHorizontalFrame(MapQuantityFrame xFrame, MapQuantityFrame yFrame, string subject, List<MapInterchangeDiagnostic> diagnostics)
    {
        var horizontal = xFrame == yFrame && xFrame is MapQuantityFrame.MapLocalXy or MapQuantityFrame.LegacyEngineXy;

        if (horizontal)
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.CoordinateFrame,
            $"{subject} positions declare X frame {xFrame} and Y frame {yFrame}; X and Y must share one horizontal frame ({nameof(MapQuantityFrame.MapLocalXy)} or {nameof(MapQuantityFrame.LegacyEngineXy)})."));
    }

    private static void RequireVerticalFrame(MapQuantityFrame zFrame, string subject, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (zFrame is MapQuantityFrame.MapLocalZ or MapQuantityFrame.LegacyEngineZ)
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.CoordinateFrame,
            $"{subject} Z position declares frame {zFrame}; it must be a vertical frame ({nameof(MapQuantityFrame.MapLocalZ)} or {nameof(MapQuantityFrame.LegacyEngineZ)})."));
    }

    private static void ValidateProvenance(MapInterchange map, HashSet<string> stableIds, List<MapInterchangeDiagnostic> diagnostics)
    {
        var provenance = map.Provenance;

        if (provenance is null)
        {
            return;
        }

        if (provenance.GeneratedFeatures is null)
        {
            AddNullComponent(diagnostics, "Provenance.GeneratedFeatures");
        }
        else
        {
            foreach (var feature in provenance.GeneratedFeatures)
            {
                if (feature is null)
                {
                    AddNullComponent(diagnostics, "Provenance.GeneratedFeatures entry");
                    continue;
                }

                RequireUniqueStableId(stableIds, feature.StableId, diagnostics);
            }
        }

        if (provenance.Sources is null)
        {
            AddNullComponent(diagnostics, "Provenance.Sources");
            return;
        }

        foreach (var source in provenance.Sources)
        {
            if (source is null)
            {
                AddNullComponent(diagnostics, "Provenance.Sources entry");
                continue;
            }

            if (source.OriginX is { } originX)
            {
                RequireQuantity(originX, $"SourceRecord '{source.Kind}' OriginX", diagnostics);
            }

            if (source.OriginY is { } originY)
            {
                RequireQuantity(originY, $"SourceRecord '{source.Kind}' OriginY", diagnostics);
            }
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

    private static void RequireQuantity(MapQuantity quantity, string field, List<MapInterchangeDiagnostic> diagnostics)
    {
        RequireFiniteQuantity(quantity, field, diagnostics);
        RequireUnitFrame(quantity, field, diagnostics);
    }

    private static void RequireUnitFrame(MapQuantity quantity, string field, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (UnitAllowed(quantity.Unit, quantity.Frame))
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.UnitFrame,
            $"{field} is declared in unit {quantity.Unit} with frame {quantity.Frame}; that frame does not accept that unit."));
    }

    private static bool UnitAllowed(MapQuantityUnit unit, MapQuantityFrame frame) => frame switch
    {
        MapQuantityFrame.SourceCrs => unit is MapQuantityUnit.Meters or MapQuantityUnit.Degrees,
        MapQuantityFrame.SourceVertical => unit == MapQuantityUnit.Meters,
        MapQuantityFrame.MapLocalXy or MapQuantityFrame.LegacyEngineXy or MapQuantityFrame.MapLocalZ or MapQuantityFrame.LegacyEngineZ => unit == MapQuantityUnit.GameUnit,
        _ => false,
    };

    private static void RequireFiniteQuantity(MapQuantity quantity, string field, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (double.IsFinite(quantity.Value))
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.QuantityFinite,
            $"{field} is {quantity.Value}; quantity values must be finite."));
    }

    private static void RequireFinitePositiveQuantity(MapQuantity quantity, string field, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (double.IsFinite(quantity.Value) && quantity.Value > 0)
        {
            return;
        }

        diagnostics.Add(new MapInterchangeDiagnostic(
            MapInterchangeDiagnostic.QuantityFinite,
            $"{field} is {quantity.Value}; it must be finite and greater than zero."));
    }

    private static void ValidateRetainedSections(MapInterchange map, List<MapInterchangeDiagnostic> diagnostics)
    {
        if (map.RetainedSections is null)
        {
            return;
        }

        foreach (var section in map.RetainedSections)
        {
            if (section is null)
            {
                AddNullComponent(diagnostics, "RetainedSections entry");
                continue;
            }

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

    private static bool HasStoredHeights(Chunk chunk, ChunkExtent extent) => chunk.Heights?.Samples is not null && chunk.Heights.Samples.Count == extent.StoredSampleCount;

    private static float? HeightAt(Chunk chunk, ChunkExtent extent, long latticeX, long latticeY)
    {
        var column = latticeX - extent.OriginX + 1;
        var row = latticeY - extent.OriginY + 1;

        if (column < 0 || column >= extent.StoredWidth || row < 0 || row >= extent.StoredHeight)
        {
            return null;
        }

        return chunk.Heights.Samples[(int)((row * extent.StoredWidth) + column)];
    }

    private static bool Conflicts(Chunk chunk, ChunkExtent extent, Chunk neighbour, ChunkExtent neighbourExtent, long latticeX, long latticeY)
    {
        var own = HeightAt(chunk, extent, latticeX, latticeY);
        var other = HeightAt(neighbour, neighbourExtent, latticeX, latticeY);

        return own is { } ownHeight && other is { } otherHeight && ownHeight != otherHeight;
    }

    private static string Describe(ChunkKey key) => $"{key.Cx}_{key.Cy}";

    private readonly record struct ChunkExtent(long OriginX, long OriginY, int CoreWidth, int CoreHeight)
    {
        public int StoredWidth => CoreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);

        public int StoredHeight => CoreHeight + (2 * MapChunkGeometry.ApronSamplesPerSide);

        public int StoredSampleCount => StoredWidth * StoredHeight;

        public int CoreCellCount => CoreWidth * CoreHeight;

        public static ChunkExtent Of(TerrainLayout layout, ChunkKey key) => new(
            (long)key.Cx * IntervalsPerChunkAxis,
            (long)key.Cy * IntervalsPerChunkAxis,
            AxisExtent(layout.LatticeWidth, key.Cx),
            AxisExtent(layout.LatticeHeight, key.Cy));

        private static int AxisExtent(int latticePoints, int index) => (int)Math.Clamp(latticePoints - ((long)index * IntervalsPerChunkAxis), 0, MapChunkGeometry.CorePointsPerAxis);
    }
}
