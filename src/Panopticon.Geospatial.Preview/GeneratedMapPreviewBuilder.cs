using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Scripting;

namespace Panopticon.Geospatial.Preview;

/// <summary>
/// Resident CPU preview builder (B4b deep dive section 6.1, WI-B4b-23): converts a
/// payload, or a chunk-aligned crop of it, into a complete playable legacy
/// <see cref="MapFile"/> in memory. Heights quantize 16-bit against the crop minimum,
/// materials paint through a <see cref="PreviewTextureMap"/>, masks set impassability,
/// and mapped Waypoint features become map objects. Dev-only: no file output, no
/// capability gating, no compatibility promise.
/// </summary>
public static class GeneratedMapPreviewBuilder
{
    /// <summary>Runtime sample spacing on the engine lattice, in meters.</summary>
    public const double SampleSpacingMeters = ChunkLayout.SampleSpacingGameUnits * UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>Runtime chunk span on the engine lattice, in meters.</summary>
    public const double ChunkSpanMeters = ChunkLayout.ChunkSpanGameUnits * UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>Legacy 16-bit height step (HeightMapData.VerticalScale of version 5 maps).</summary>
    public const double HeightQuantumGameUnits = 0.0390625;

    /// <summary>Tile values one standard CellSize 4 texture occupies in BlendTileData.</summary>
    private const int TileValuesPerTexture = 64;

    public static GeneratedMapPreviewResult Build(
        IGeneratedMapPayloadReader reader,
        PreviewTextureMap textureMap,
        PreviewCrop? crop = null,
        PreviewBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(textureMap);

        var resolvedOptions = options ?? new PreviewBuildOptions();
        var diagnostics = new GeoDiagnosticBag();
        var grid = reader.Metadata.Grid;
        var resolvedCrop = crop ?? PreviewCrop.WholeMap(grid.ChunksX, grid.ChunksY);

        if (!TryValidateCrop(resolvedCrop, grid, diagnostics)
            || !TryValidateResidentLimit(resolvedCrop, resolvedOptions, diagnostics))
        {
            return Failure(diagnostics);
        }

        var textureTable = TryResolvePaletteTextures(reader.Metadata.Palette, textureMap, diagnostics);
        if (textureTable is null)
        {
            return Failure(diagnostics);
        }

        // Core lattice dimensions: shared chunk edges store once, so the final edge adds one point.
        var coreWidth = ((long)resolvedCrop.ChunkXHi - resolvedCrop.ChunkXLo) * ChunkLayout.IntervalsPerChunk + 1;
        var coreHeight = ((long)resolvedCrop.ChunkYHi - resolvedCrop.ChunkYLo) * ChunkLayout.IntervalsPerChunk + 1;

        var heights = new float[coreWidth, coreHeight];
        var materialIndices = new byte[coreWidth, coreHeight];
        var maskFlags = new TerrainMaskFlags[coreWidth, coreHeight];

        if (!TryStitchChunks(reader, resolvedCrop, heights, materialIndices, maskFlags, diagnostics))
        {
            return Failure(diagnostics);
        }

        var cropMinimumGameZ = (double)float.MaxValue;
        foreach (var height in heights)
        {
            if (height < cropMinimumGameZ)
            {
                cropMinimumGameZ = height;
            }
        }

        var waypoints = CollectWaypoints(reader.Metadata.Features, resolvedCrop, coreWidth, coreHeight, cropMinimumGameZ, diagnostics);
        if (waypoints is null)
        {
            return Failure(diagnostics);
        }

        var border = resolvedOptions.BorderWidth;
        var storedWidth = coreWidth + (2L * border);
        var storedHeight = coreHeight + (2L * border);
        var stored = new ushort[storedWidth, storedHeight];
        var clampedCount = 0L;
        var maxQuantizationError = 0d;

        for (var y = 0; y < coreHeight; y++)
        {
            for (var x = 0; x < coreWidth; x++)
            {
                var quantized = Quantize(heights[x, y], cropMinimumGameZ, out var clamped);
                if (clamped)
                {
                    clampedCount++;
                }

                var restoredError = Math.Abs((quantized * HeightQuantumGameUnits) - (heights[x, y] - cropMinimumGameZ));
                if (restoredError > maxQuantizationError)
                {
                    maxQuantizationError = restoredError;
                }

                stored[x + border, y + border] = quantized;
            }
        }

        ExtendBorderPadding(stored, border, coreWidth, coreHeight);

        var mapFile = MapFile.CreateFromHeights(stored, border, textureTable.Textures[0]);
        mapFile.BlendTileData.ReplaceTextures(textureTable.Textures);
        PaintBlendTiles(mapFile.BlendTileData, textureTable, materialIndices, maskFlags, border, coreWidth, coreHeight, out var impassableCount, out var unbuildableCount);
        ApplyWaypoints(mapFile, waypoints);

        var transform = new PreviewTransform(
            resolvedCrop.ChunkXLo * (double)ChunkLayout.ChunkSpanGameUnits,
            resolvedCrop.ChunkYLo * (double)ChunkLayout.ChunkSpanGameUnits,
            cropMinimumGameZ,
            HeightQuantumGameUnits);

        var report = new GeneratedMapPreviewReport(
            resolvedCrop.ChunkXLo,
            resolvedCrop.ChunkYLo,
            resolvedCrop.ChunkXHi,
            resolvedCrop.ChunkYHi,
            (uint)storedWidth,
            (uint)storedHeight,
            border,
            textureTable.Textures,
            impassableCount,
            unbuildableCount,
            clampedCount,
            maxQuantizationError,
            waypoints.ConvertedCount,
            waypoints.ExcludedCount,
            waypoints.OverriddenStartSlots,
            CountUnrepresentedFeatures(reader.Metadata.Features));

        return new GeneratedMapPreviewResult(mapFile, transform, report, diagnostics.Items);
    }

    private static bool TryValidateCrop(PreviewCrop crop, PayloadGridDefinition grid, GeoDiagnosticBag diagnostics)
    {
        if (crop.ChunkXLo < 0 || crop.ChunkYLo < 0
            || crop.ChunkXHi > grid.ChunksX || crop.ChunkYHi > grid.ChunksY
            || crop.ChunkXLo >= crop.ChunkXHi || crop.ChunkYLo >= crop.ChunkYHi)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.CropInvalid,
                GeoStage.Generate,
                GeoSeverity.Error,
                $"The crop [{crop.ChunkXLo}, {crop.ChunkXHi}) x [{crop.ChunkYLo}, {crop.ChunkYHi}) is not a non-empty chunk rectangle inside the {grid.ChunksX}x{grid.ChunksY} chunk grid."));
            return false;
        }

        return true;
    }

    /// <summary>Resident limit check runs in long arithmetic before any allocation or dimension cast.</summary>
    private static bool TryValidateResidentLimit(PreviewCrop crop, PreviewBuildOptions options, GeoDiagnosticBag diagnostics)
    {
        var coreWidth = ((long)crop.ChunkXHi - crop.ChunkXLo) * ChunkLayout.IntervalsPerChunk + 1;
        var coreHeight = ((long)crop.ChunkYHi - crop.ChunkYLo) * ChunkLayout.IntervalsPerChunk + 1;
        var storedWidth = coreWidth + (2L * options.BorderWidth);
        var storedHeight = coreHeight + (2L * options.BorderWidth);
        var storedCells = storedWidth * storedHeight;

        if (storedCells > options.MaxStoredHeightCells || storedWidth > int.MaxValue || storedHeight > int.MaxValue)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.PreviewOversized,
                GeoStage.Generate,
                GeoSeverity.Error,
                $"The crop needs {storedCells} stored height cells (border included), above the limit {options.MaxStoredHeightCells}; choose a smaller chunk crop, the map is never cropped automatically."));
            return false;
        }

        return true;
    }

    private sealed record PaletteTextureTable(string[] Textures, int[] OrdinalByPaletteIndex);

    private static PaletteTextureTable? TryResolvePaletteTextures(MaterialPalette palette, PreviewTextureMap textureMap, GeoDiagnosticBag diagnostics)
    {
        var missing = new List<string>();
        var unknown = new List<string>();
        var canonicalByIndex = new string?[palette.Count];

        for (var index = 0; index < palette.Count; index++)
        {
            var name = palette.Names[index];
            switch (textureMap.TryResolve(name, out var canonical))
            {
                case PreviewTextureResolution.MissingBinding:
                    missing.Add(name);
                    break;
                case PreviewTextureResolution.UnknownTextureName:
                    unknown.Add(name);
                    break;
                default:
                    canonicalByIndex[index] = canonical;
                    break;
            }
        }

        if (missing.Count > 0)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.TextureMapMissingBinding,
                GeoStage.Generate,
                GeoSeverity.Error,
                $"Every payload palette entry requires a texture binding; missing: {string.Join(", ", missing)}."));
        }

        if (unknown.Count > 0)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.TextureMapUnknownTexture,
                GeoStage.Generate,
                GeoSeverity.Error,
                $"Bound texture names unknown to the catalog: {string.Join(", ", unknown)}."));
        }

        if (missing.Count > 0 || unknown.Count > 0)
        {
            return null;
        }

        // Distinct canonical names in palette order form the texture table.
        var textures = new List<string>(canonicalByIndex.Length);
        var ordinalByIndex = new int[canonicalByIndex.Length];
        foreach (var (canonical, index) in canonicalByIndex.Select((value, index) => (value, index)))
        {
            var ordinal = textures.IndexOf(canonical!);
            if (ordinal < 0)
            {
                ordinal = textures.Count;
                textures.Add(canonical!);
            }

            ordinalByIndex[index] = ordinal;
        }

        return new PaletteTextureTable([.. textures], ordinalByIndex);
    }

    private static bool TryStitchChunks(
        IGeneratedMapPayloadReader reader,
        PreviewCrop crop,
        float[,] heights,
        byte[,] materialIndices,
        TerrainMaskFlags[,] maskFlags,
        GeoDiagnosticBag diagnostics)
    {
        for (var chunkY = crop.ChunkYLo; chunkY < crop.ChunkYHi; chunkY++)
        {
            for (var chunkX = crop.ChunkXLo; chunkX < crop.ChunkXHi; chunkX++)
            {
                var terrain = reader.ReadTerrain(chunkX, chunkY, diagnostics);
                var materials = reader.ReadMaterial(chunkX, chunkY, diagnostics);
                var masks = reader.ReadMask(chunkX, chunkY, diagnostics);

                if (terrain is null || materials is null || masks is null)
                {
                    return false;
                }

                for (var localY = 0; localY < ChunkLayout.PointsPerChunkAxis; localY++)
                {
                    for (var localX = 0; localX < ChunkLayout.PointsPerChunkAxis; localX++)
                    {
                        var x = ((chunkX - crop.ChunkXLo) * ChunkLayout.IntervalsPerChunk) + localX;
                        var y = ((chunkY - crop.ChunkYLo) * ChunkLayout.IntervalsPerChunk) + localY;

                        // Stored chunk samples carry an apron per side; core lattice point
                        // (localX, localY) is stored position (localX + 1, localY + 1).
                        // Edge point 128 of a chunk duplicates its neighbour's point 0 and
                        // overwrites it with the seam-equal value.
                        heights[x, y] = terrain.GetHeight(localX + 1, localY + 1);
                        materialIndices[x, y] = materials.GetIndex(localX, localY);
                        maskFlags[x, y] = masks.GetFlags(localX, localY);
                    }
                }
            }
        }

        return !diagnostics.HasErrors;
    }

    private static void PaintBlendTiles(
        BlendTileData blendTiles,
        PaletteTextureTable textureTable,
        byte[,] materialIndices,
        TerrainMaskFlags[,] maskFlags,
        uint border,
        long coreWidth,
        long coreHeight,
        out long impassableCount,
        out long unbuildableCount)
    {
        impassableCount = 0L;
        unbuildableCount = 0L;

        for (var y = 0; y < coreHeight; y++)
        {
            for (var x = 0; x < coreWidth; x++)
            {
                var ordinal = textureTable.OrdinalByPaletteIndex[materialIndices[x, y]];
                blendTiles.Tiles[x + border, y + border] = (ushort)(ordinal * TileValuesPerTexture);

                var flags = maskFlags[x, y];
                if ((flags & TerrainMaskFlags.Impassable) != 0)
                {
                    blendTiles.Impassability[x + border, y + border] = true;
                    impassableCount++;
                }

                if ((flags & TerrainMaskFlags.Unbuildable) != 0)
                {
                    // The authoritative payload bit is retained unconverted: legacy
                    // Buildability polarity is unverified and never implies impassable.
                    unbuildableCount++;
                }
            }
        }

        // Border tile cells extend the nearest core edge like the border heights;
        // border impassability stays clear, the padding carries no payload data.
        ExtendBorderPadding(blendTiles.Tiles, border, coreWidth, coreHeight);
    }

    private static void ExtendBorderPadding(ushort[,] stored, uint border, long coreWidth, long coreHeight)
    {
        for (var y = 0L; y < coreHeight + (2L * border); y++)
        {
            long coreY;
            if (y < border)
            {
                coreY = 0;
            }
            else if (y >= border + coreHeight)
            {
                coreY = coreHeight - 1;
            }
            else
            {
                coreY = y - border;
            }

            for (var x = 0L; x < coreWidth + (2L * border); x++)
            {
                if (x >= border && x < border + coreWidth && y >= border && y < border + coreHeight)
                {
                    continue;
                }

                long coreX;
                if (x < border)
                {
                    coreX = 0;
                }
                else if (x >= border + coreWidth)
                {
                    coreX = coreWidth - 1;
                }
                else
                {
                    coreX = x - border;
                }

                stored[x, y] = stored[coreX + border, coreY + border];
            }
        }
    }

    private sealed record WaypointPlan(
        Dictionary<int, (double EngineX, double EngineY)> AuthoredStarts,
        List<(string Name, double EngineX, double EngineY, double RebasedZ)> OrdinaryWaypoints,
        int ExcludedCount,
        int[] OverriddenStartSlots)
    {
        public int ConvertedCount => AuthoredStarts.Count + OrdinaryWaypoints.Count;
    }

    private static WaypointPlan? CollectWaypoints(
        IReadOnlyList<GeneratedFeature> features,
        PreviewCrop crop,
        long coreWidth,
        long coreHeight,
        double cropMinimumGameZ,
        GeoDiagnosticBag diagnostics)
    {
        var originX = crop.ChunkXLo * ChunkSpanMeters;
        var originY = crop.ChunkYLo * ChunkSpanMeters;
        var maxEngineX = (coreWidth - 1) * SampleSpacingMeters;
        var maxEngineY = (coreHeight - 1) * SampleSpacingMeters;

        var authoredStarts = new Dictionary<int, (double EngineX, double EngineY)>();
        var ordinary = new List<(string Name, double EngineX, double EngineY, double RebasedZ)>();
        var seenOrdinaryNames = new HashSet<string>(StringComparer.Ordinal);
        var seenStartSlots = new HashSet<int>();
        var excludedCount = 0;
        var duplicate = false;

        foreach (var feature in features)
        {
            if (feature.Kind != GeneratedFeatureKind.Waypoint || feature.Position is null)
            {
                continue;
            }

            var name = feature.Attributes.TryGetValue("name", out var attributeName) && !string.IsNullOrWhiteSpace(attributeName)
                ? attributeName
                : feature.FeatureId;
            var engineX = (feature.Position.Value.X * UnitConversionPolicy.GeneralsFeetToMeters) - originX;
            var engineY = (feature.Position.Value.Y * UnitConversionPolicy.GeneralsFeetToMeters) - originY;

            // Inclusive of the outer world edge; anything past the stored lattice is outside the crop.
            if (engineX < 0d || engineX > maxEngineX || engineY < 0d || engineY > maxEngineY)
            {
                excludedCount++;
                continue;
            }

            var startSlot = name.Equals("Player_1_Start", StringComparison.OrdinalIgnoreCase) ? 1
                : name.Equals("Player_2_Start", StringComparison.OrdinalIgnoreCase) ? 2
                : 0;
            if (startSlot != 0)
            {
                if (!seenStartSlots.Add(startSlot))
                {
                    diagnostics.Add(new GeoDiagnostic(
                        PreviewDiagnosticCodes.WaypointNameDuplicate,
                        GeoStage.Generate,
                        GeoSeverity.Error,
                        $"The reserved player start '{name}' is authored more than once."));
                    duplicate = true;
                }
                else
                {
                    authoredStarts[startSlot] = (engineX, engineY);
                }
            }
            else
            {
                if (!seenOrdinaryNames.Add(name))
                {
                    diagnostics.Add(new GeoDiagnostic(
                        PreviewDiagnosticCodes.WaypointNameDuplicate,
                        GeoStage.Generate,
                        GeoSeverity.Error,
                        $"The waypoint name '{name}' is authored more than once."));
                    duplicate = true;
                }
                else
                {
                    // Player starts use Z zero; ordinary waypoints keep the rebased absolute elevation.
                    ordinary.Add((name, engineX, engineY, (feature.Position.Value.Z - cropMinimumGameZ) * UnitConversionPolicy.GeneralsFeetToMeters));
                }
            }
        }

        if (duplicate)
        {
            return null;
        }

        return new WaypointPlan(authoredStarts, ordinary, excludedCount, [.. authoredStarts.Keys.Order()]);
    }

    private static void ApplyWaypoints(MapFile mapFile, WaypointPlan plan)
    {
        foreach (var slot in plan.OverriddenStartSlots)
        {
            RemoveFactoryStart(mapFile, slot);
            var start = plan.AuthoredStarts[slot];

            // Z stays zero: skirmish startup adds the terrain height itself.
            mapFile.ObjectsList.AddObject(
                Waypoint.CreateWaypointMapObject(new Vector3((float)start.EngineX, (float)start.EngineY, 0f), slot, $"Player_{slot}_Start"));
        }

        var nextWaypointId = 3;
        foreach (var waypoint in plan.OrdinaryWaypoints)
        {
            mapFile.ObjectsList.AddObject(
                Waypoint.CreateWaypointMapObject(
                    new Vector3((float)waypoint.EngineX, (float)waypoint.EngineY, (float)waypoint.RebasedZ),
                    nextWaypointId++,
                    waypoint.Name));
        }
    }

    private static void RemoveFactoryStart(MapFile mapFile, int slot)
    {
        var canonicalName = $"Player_{slot}_Start";
        MapObject? factoryStart = null;

        foreach (var mapObject in mapFile.ObjectsList.Objects)
        {
            if (mapObject.TypeName == Waypoint.ObjectTypeName
                && mapObject.Properties.TryGetValue("waypointName", out var nameProperty)
                && nameProperty.Value is string name
                && name == canonicalName)
            {
                factoryStart = mapObject;
                break;
            }
        }

        if (factoryStart is not null)
        {
            mapFile.ObjectsList.RemoveObject(factoryStart);
        }
    }

    private static List<PreviewFeatureKindCount> CountUnrepresentedFeatures(IReadOnlyList<GeneratedFeature> features)
    {
        var counts = new Dictionary<GeneratedFeatureKind, long>();
        foreach (var kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            counts[kind] = 0;
        }

        foreach (var feature in features)
        {
            if (feature.Kind != GeneratedFeatureKind.Waypoint || feature.Position is null)
            {
                counts[feature.Kind]++;
            }
        }

        return [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PreviewFeatureKindCount(kind, counts[kind]))];
    }

    private static ushort Quantize(float heightGameUnits, double cropMinimumGameZ, out bool clamped)
    {
        var levels = Math.Round((heightGameUnits - cropMinimumGameZ) / HeightQuantumGameUnits, MidpointRounding.AwayFromZero);
        clamped = false;

        if (levels < 0d)
        {
            clamped = true;
            return 0;
        }

        if (levels > ushort.MaxValue)
        {
            clamped = true;
            return ushort.MaxValue;
        }

        return (ushort)levels;
    }

    private static GeneratedMapPreviewResult Failure(GeoDiagnosticBag diagnostics)
    {
        return new GeneratedMapPreviewResult(null, null, null, diagnostics.Items);
    }
}
