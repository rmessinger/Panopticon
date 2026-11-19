using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using GeneratedFeature = Panopticon.Geospatial.Features.GeneratedFeature;
using MapGeneratedFeature = Panopticon.Content.Importing.Maps.GeneratedFeature;

namespace Panopticon.Geospatial.Preview;

public static class GeneratedMapPackageConverter
{
    private const string TerrainAssetScope = "terrain";
    private const string LegacyAssetScope = "legacy";
    private const string ControlPointCategory = "control-point";
    private const int HeightSectionVersion = 5;
    private const string HeightQuantizationLimitation =
        "Terrain heights are quantized to the resident height grid (0.0390625 game height units per step, round half to even); stored heights differ from the payload by at most half a step where the payload is off-grid.";

    private static readonly DecoderOptions DecodeOptions = new() { SkipMetadata = true };

    private static readonly IReadOnlyList<string> Limitations =
    [
        "Palette textures are material names in the terrain asset scope; gameplay texture binding is not performed.",
        "Imagery keeps payload PNG row order (north row first).",
        "Line and polygon features are stored in the package vector layer; provenance keeps one entry per source part.",
        "Conversion holds chunk data in memory; streaming is not supported.",
        "The import report is an import result, not package content; a reloaded package carries an empty import-report placeholder by design.",
    ];

    public static GeneratedMapPackageResult Convert(
        IGeneratedMapPayloadReader reader,
        string mapName,
        string targetPackageRoot,
        PreviewTextureMap? textureMap = null,
        IReadOnlyList<GeneratedMapSkirmishSlot>? skirmishSlots = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);

        var metadata = reader.Metadata;
        var grid = metadata.Grid;
        var payloadReport = reader.Report;
        var sampling = payloadReport.Terrain.Sampling;

        if (grid.TotalChunkCount > int.MaxValue)
        {
            return Failed([Format(
                GeneratedMapPackageDiagnosticCodes.ChunkCountOverflow,
                $"The grid declares {grid.TotalChunkCount} chunks; at most {int.MaxValue} fit in memory.")]);
        }

        if (sampling.OutsideFillCount > int.MaxValue || sampling.NoDataFillCount > int.MaxValue)
        {
            return Failed([Format(
                GeneratedMapPackageDiagnosticCodes.FillCountOverflow,
                $"The fill counts ({sampling.OutsideFillCount} outside, {sampling.NoDataFillCount} no-data) exceed the report's integer range.")]);
        }

        var latticeWidth = grid.ChunksX * ChunkLayout.IntervalsPerChunk + 1;
        var latticeHeight = grid.ChunksY * ChunkLayout.IntervalsPerChunk + 1;
        var mapArea = (long)latticeWidth * latticeHeight;

        if (mapArea > uint.MaxValue)
        {
            return Failed([Format(
                GeneratedMapPackageDiagnosticCodes.LatticeAreaOverflow,
                $"The {latticeWidth} x {latticeHeight} lattice exceeds the map area limit of {uint.MaxValue} points.")]);
        }

        var diagnostics = new List<string>();
        var chunks = new List<Chunk>();
        var heightQuantizer = new HeightQuantizer();

        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                var chunk = ReadChunk(reader, grid.ImageryPixelsPerSample is not null, chunkX, chunkY, heightQuantizer, diagnostics);

                if (chunk is null)
                {
                    return Failed(diagnostics);
                }

                chunks.Add(chunk);
            }
        }

        if (heightQuantizer.RejectedCount > 0)
        {
            return Failed([Format(
                GeneratedMapPackageDiagnosticCodes.HeightQuantization,
                $"{heightQuantizer.RejectedCount} of {heightQuantizer.SampleCount} terrain samples are non-finite or outside the range the resident height grid represents; no package was written.")]);
        }

        var (vectorLayers, vectorConflicts, vectorDiagnostics) = BuildVectorLayers(metadata.Features);

        if (vectorConflicts.Count > 0)
        {
            return Failed(vectorConflicts);
        }

        // Buildings consume the same original emissions as BuildVectorLayers, before
        // provenance dedupe, so one source part mapped into several mask features still
        // yields one building part.
        var (buildings, buildingDiagnostics, buildingConflicts) = GeneratedBuildingBuilder.Build(metadata.Features, grid, chunks);

        if (buildingConflicts.Count > 0)
        {
            return Failed(buildingConflicts);
        }

        var (features, provenanceFeatures, dedupeDiagnostics, sourceConflicts) = DedupeFeatures(metadata.Features);

        if (sourceConflicts.Count > 0)
        {
            return Failed(sourceConflicts);
        }

        var blendDiagnostics = new List<string>();
        var blendTileData = BuildBlendTileData(metadata, chunks, textureMap, latticeWidth, latticeHeight, blendDiagnostics);

        if (blendTileData is null)
        {
            return Failed(blendDiagnostics);
        }

        // The skirmish opt-in is absent by default: no synthesis, no extra waypoints, and the
        // written package stays byte-for-byte the one the pre-skirmish converter produced.
        var skirmishWaypoints = new List<PlacedObject>();
        var retainedSections = new List<RetainedSection>();
        var skirmishDiagnostics = new List<string>();

        if (skirmishSlots is { Count: > 0 })
        {
            var factions = new List<string>(skirmishSlots.Count);

            if (!TryBuildSkirmishStarts(skirmishSlots, chunks, latticeWidth, latticeHeight, skirmishWaypoints, factions, skirmishDiagnostics))
            {
                return Failed(skirmishDiagnostics);
            }

            retainedSections.Add(LegacyMapPackageImporter.CreateSkirmishSidesSection(factions));
            skirmishDiagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.SkirmishSynthesized,
                $"Synthesized the retained SidesList section with the factions {string.Join(", ", factions)} and the player start waypoints {StartListText(skirmishWaypoints)}."));
        }

        var interchange = new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity(mapName, MapSourceKind.Generated, null),
            BuildLayout(grid, chunks, latticeWidth, latticeHeight, (uint)mapArea),
            BuildBlend(metadata),
            BuildObjects(features, skirmishWaypoints),
            BuildWaypoints(features),
            BuildProvenance(metadata, provenanceFeatures),
            BuildImportReport(payloadReport),
            retainedSections,
            blendTileData: blendTileData,
            globalLighting: BuildGeneratedLighting(),
            waypointPaths: new MapWaypointPathsRecord(0, []),
            vectorLayers: vectorLayers,
            buildings: buildings);

        var validation = MapInterchangeValidator.Validate(interchange);

        if (validation.Count > 0)
        {
            return Failed(validation.Select(diagnostic => Format(diagnostic.Code, diagnostic.Message)));
        }

        var write = LegacyMapPackageImporter.Write(interchange, targetPackageRoot);

        if (!write.Success)
        {
            return Failed(write.Diagnostics.Select(diagnostic => Format(diagnostic.Code, diagnostic.Message)));
        }

        var heightDiagnostics = new List<string>();
        var limitations = Limitations;

        if (heightQuantizer.ChangedCount > 0)
        {
            var maxError = heightQuantizer.MaxError.ToString("R", CultureInfo.InvariantCulture);
            heightDiagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.HeightQuantizationSummary,
                $"Quantized {heightQuantizer.ChangedCount} of {heightQuantizer.SampleCount} terrain samples to the resident height grid (step 0.0390625 game height units); max error {maxError} game height units, bound 0.01953125."));
            limitations = [.. Limitations, HeightQuantizationLimitation];
        }

        return new GeneratedMapPackageResult(true, dedupeDiagnostics.Concat(vectorDiagnostics).Concat(buildingDiagnostics).Concat(skirmishDiagnostics).Concat(heightDiagnostics).ToArray(), interchange, targetPackageRoot, chunks.Count, limitations);
    }

    private sealed class HeightQuantizer
    {
        private const double Step = 0.0390625d;
        private const double LowestAcceptedQuotient = -0.5d;
        private const double HighestRejectedQuotient = 65535.5d;

        public long SampleCount { get; private set; }

        public long RejectedCount { get; private set; }

        public long ChangedCount { get; private set; }

        public double MaxError { get; private set; }

        public float[] Quantize(ReadOnlySpan<float> source)
        {
            var quantized = new float[source.Length];

            for (var index = 0; index < source.Length; index++)
            {
                var value = source[index];
                var quotient = (double)value / Step;
                SampleCount++;

                if (!double.IsFinite(quotient) || quotient < LowestAcceptedQuotient || quotient >= HighestRejectedQuotient)
                {
                    RejectedCount++;
                    quantized[index] = value;
                    continue;
                }

                var elevation = (ushort)Math.Round(quotient, MidpointRounding.ToEven);
                var result = (float)(elevation * Step);
                quantized[index] = result;

                if (result != value)
                {
                    ChangedCount++;
                    MaxError = Math.Max(MaxError, Math.Abs((double)result - value));
                }
            }

            return quantized;
        }
    }

    private static Chunk? ReadChunk(
        IGeneratedMapPayloadReader reader,
        bool hasImagery,
        int chunkX,
        int chunkY,
        HeightQuantizer heightQuantizer,
        List<string> diagnostics)
    {
        var terrain = ReadLayer(bag => reader.ReadTerrain(chunkX, chunkY, bag), "terrain", chunkX, chunkY, diagnostics);

        if (terrain is null)
        {
            return null;
        }

        var materials = ReadLayer(bag => reader.ReadMaterial(chunkX, chunkY, bag), "material", chunkX, chunkY, diagnostics);

        if (materials is null)
        {
            return null;
        }

        var masks = ReadLayer(bag => reader.ReadMask(chunkX, chunkY, bag), "mask", chunkX, chunkY, diagnostics);

        if (masks is null)
        {
            return null;
        }

        ImageryLayer? imagery = null;

        if (hasImagery)
        {
            var imageryChunk = ReadLayer(bag => reader.ReadImagery(chunkX, chunkY, bag), "imagery", chunkX, chunkY, diagnostics);

            if (imageryChunk is null)
            {
                return null;
            }

            imagery = DecodeImagery(imageryChunk, chunkX, chunkY, diagnostics);

            if (imagery is null)
            {
                return null;
            }
        }

        return new Chunk(
            new ChunkKey(chunkX, chunkY),
            new HeightLayer(heightQuantizer.Quantize(terrain.Heights.Span)),
            new MaterialLayer(materials.Indices.ToArray()),
            new MaskLayer(masks.Flags.ToArray()),
            imagery);
    }

    private static TLayer? ReadLayer<TLayer>(
        Func<GeoDiagnosticBag, TLayer?> read,
        string layerName,
        int chunkX,
        int chunkY,
        List<string> diagnostics)
        where TLayer : class
    {
        var bag = new GeoDiagnosticBag();
        var layer = read(bag);
        var errors = bag.Items.Where(diagnostic => diagnostic.Severity == GeoSeverity.Error).ToArray();

        if (errors.Length > 0)
        {
            diagnostics.AddRange(errors.Select(diagnostic => Format(diagnostic.Code, diagnostic.Message)));
            return null;
        }

        if (layer is null)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.ChunkReadFailed,
                $"Chunk ({chunkX}, {chunkY}): the reader returned no {layerName} layer."));
            return null;
        }

        return layer;
    }

    private static ImageryLayer? DecodeImagery(ImageryChunk imagery, int chunkX, int chunkY, List<string> diagnostics)
    {
        var pixelsPerAxis = imagery.PixelsPerAxis;
        var rgbaBytes = (long)pixelsPerAxis * pixelsPerAxis * 4;

        if (rgbaBytes > int.MaxValue)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.ImageryOversized,
                $"Chunk ({chunkX}, {chunkY}): {pixelsPerAxis} x {pixelsPerAxis} imagery exceeds the largest allocatable RGBA buffer."));
            return null;
        }

        Image<Rgba32> image;

        try
        {
            image = Image.Load<Rgba32>(DecodeOptions, imagery.Png.Span);
        }
        catch (Exception ex) when (ex is ImageFormatException or InvalidImageContentException or InvalidDataException)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.ImageryDecodeFailed,
                $"Chunk ({chunkX}, {chunkY}): the PNG imagery failed to decode: {ex.Message}"));
            return null;
        }

        using (image)
        {
            if (image.Frames.Count != 1)
            {
                diagnostics.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.ImageryDecodeFailed,
                    $"Chunk ({chunkX}, {chunkY}): the PNG carries {image.Frames.Count} frames; exactly one still image is required."));
                return null;
            }

            if (image.Width != pixelsPerAxis || image.Height != pixelsPerAxis)
            {
                diagnostics.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.ImageryDimensionsMismatch,
                    $"Chunk ({chunkX}, {chunkY}): the decoded imagery is {image.Width} x {image.Height} pixels but {pixelsPerAxis} x {pixelsPerAxis} were expected."));
                return null;
            }

            var rgba = new byte[rgbaBytes];
            image.ProcessPixelRows(accessor => CopyRows(accessor, rgba, pixelsPerAxis));

            return new ImageryLayer(pixelsPerAxis, pixelsPerAxis, rgba);
        }
    }

    private static void CopyRows(PixelAccessor<Rgba32> accessor, byte[] rgba, int pixelsPerAxis)
    {
        for (var y = 0; y < pixelsPerAxis; y++)
        {
            var row = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
            row.CopyTo(rgba.AsSpan(y * pixelsPerAxis * 4));
        }
    }

    private static TerrainLayout BuildLayout(
        PayloadGridDefinition grid,
        IReadOnlyList<Chunk> chunks,
        int latticeWidth,
        int latticeHeight,
        uint mapArea)
    {
        var transform = grid.Transform;

        return new TerrainLayout(
            new MapQuantity(ChunkLayout.SampleSpacingGameUnits, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticeWidth,
            latticeHeight,
            grid.ChunksX,
            grid.ChunksY,
            0,
            new GeospatialTransform(
                transform.ProjectedCoordinateSystem.ToString(),
                new MapQuantity(transform.OriginEastingNorthing.X, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
                new MapQuantity(transform.OriginEastingNorthing.Y, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
                transform.MetersPerGameUnit,
                transform.VerticalDatum,
                new MapQuantity(transform.VerticalOriginMeters, MapQuantityUnit.Meters, MapQuantityFrame.SourceVertical),
                transform.VerticalExaggeration),
            MapNodataPolicy.Fail,
            HeightSectionVersion,
            mapArea,
            [new MapTerrainBorder(0, 0, (uint)(latticeWidth - 1), (uint)(latticeHeight - 1))],
            chunks);
    }

    private static BlendModel BuildBlend(GeneratedMapPayloadMetadata metadata) =>
        new(metadata.Palette.Names
            .Select((name, index) => new MaterialPaletteEntry((byte)index, name, new AssetReference(TerrainAssetScope, name)))
            .ToArray());

    // Copies GlobalLighting.CreateForGeneratedMap (Simulation Data/Map/GlobalLighting.cs:59), which is internal to Simulation.
    private static MapGlobalLightingRecord BuildGeneratedLighting()
    {
        var sun = new MapGlobalLightRecord(
            new System.Numerics.Vector3(0.38f, 0.38f, 0.38f),
            new System.Numerics.Vector3(0.52f, 0.51f, 0.49f),
            System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.50f, 0.35f, -0.79f)));
        var fill = new MapGlobalLightRecord(
            new System.Numerics.Vector3(0.04f, 0.04f, 0.04f),
            new System.Numerics.Vector3(0.08f, 0.08f, 0.08f),
            System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-0.55f, -0.40f, -0.73f)));
        var configuration = new MapLightingConfigurationRecord(sun, fill, null, sun, fill, null, null, null, null);

        // The engine TimeOfDay is 1-based and the typed Time carries that raw value, as LegacyMapReader does.
        return new MapGlobalLightingRecord(
            6,
            (MapTimeOfDay)(uint)Panopticon.Data.Map.TimeOfDay.Morning,
            new Dictionary<MapTimeOfDay, MapLightingConfigurationRecord>
            {
                [MapTimeOfDay.Morning] = configuration,
                [MapTimeOfDay.Afternoon] = configuration,
                [MapTimeOfDay.Evening] = configuration,
                [MapTimeOfDay.Night] = configuration,
            },
            0);
    }

    private static MapBlendTileData? BuildBlendTileData(
        GeneratedMapPayloadMetadata metadata,
        IReadOnlyList<Chunk> chunks,
        PreviewTextureMap? textureMap,
        int latticeWidth,
        int latticeHeight,
        List<string> diagnostics)
    {
        var palette = metadata.Palette;

        if (palette.Count == 0)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.BlendTextureUnresolved,
                "The payload palette is empty; no terrain texture can be materialized; no package was written."));
            return null;
        }

        var canonicalByIndex = new string[palette.Count];
        var unresolved = new List<string>();

        for (var index = 0; index < palette.Count; index++)
        {
            var name = palette.Names[index];
            string? canonical = null;
            var resolution = textureMap is null
                ? PreviewTextureResolution.MissingBinding
                : textureMap.TryResolve(name, out canonical);

            if (resolution == PreviewTextureResolution.Resolved)
            {
                canonicalByIndex[index] = canonical!;
            }
            else
            {
                unresolved.Add(name);
            }
        }

        if (unresolved.Count > 0)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.BlendTextureUnresolved,
                $"Palette entries without a resolvable texture binding: {string.Join(", ", unresolved)}; no package was written."));
            return null;
        }

        var table = GeneratedPaletteBlend.FromCanonicalNames(canonicalByIndex);
        var materialIndices = new byte[latticeWidth, latticeHeight];
        var maskFlags = new TerrainMaskFlags[latticeWidth, latticeHeight];

        foreach (var chunk in chunks)
        {
            var originX = chunk.Key.Cx * ChunkLayout.IntervalsPerChunk;
            var originY = chunk.Key.Cy * ChunkLayout.IntervalsPerChunk;
            var coreWidth = Math.Min(ChunkLayout.PointsPerChunkAxis, latticeWidth - originX);
            var coreHeight = Math.Min(ChunkLayout.PointsPerChunkAxis, latticeHeight - originY);
            var materials = chunk.Materials!.Indices;
            var masks = chunk.Masks!.Flags;

            for (var y = 0; y < coreHeight; y++)
            {
                for (var x = 0; x < coreWidth; x++)
                {
                    materialIndices[originX + x, originY + y] = materials[(y * coreWidth) + x];
                    maskFlags[originX + x, originY + y] = (TerrainMaskFlags)masks[(y * coreWidth) + x];
                }
            }
        }

        var tiles = new ushort[latticeWidth, latticeHeight];
        var impassability = new bool[latticeWidth, latticeHeight];

        GeneratedPaletteBlend.PaintTiles(
            tiles,
            impassability,
            table,
            materialIndices,
            maskFlags,
            0,
            latticeWidth,
            latticeHeight,
            out _,
            out _);

        return new MapBlendTileData(
            Version: 8,
            NumTiles: (uint)((long)latticeWidth * latticeHeight),
            Tiles: tiles,
            Blends: new uint[latticeWidth, latticeHeight],
            ThreeWayBlends: new uint[latticeWidth, latticeHeight],
            CliffTextures: new uint[latticeWidth, latticeHeight],
            Impassability: impassability,
            TextureCellCount: 16,
            Textures: table.Textures.Select(name => new MapBlendTextureRecord(name, 0, 16, 4, 0)).ToArray(),
            MagicValue1: 0,
            MagicValue2: 0,
            BlendDescriptions: [],
            ParsedCliffTextureMappingsCount: 0,
            CliffTextureMappings: []);
    }

    // One source feature can be staged twice by overlapping mappings, and one source part can
    // be mapped into several mask features (for example passability plus buildability); the
    // interchange validator rejects every repeated stable id. Exact duplicates collapse to one
    // feature, same-part provenance projections collapse to one entry when their source
    // geometry and source attributes are identical, and conflicts that touch placed objects or
    // waypoints still fail validation unchanged. Keep-first is the explicit input-order policy
    // (mapper rules are ordered); permutation invariance is not claimed.
    private static (IReadOnlyList<GeneratedFeature> Features, IReadOnlyList<GeneratedFeature> ProvenanceFeatures, IReadOnlyList<string> Diagnostics, IReadOnlyList<string> SourceConflicts) DedupeFeatures(
        IReadOnlyList<GeneratedFeature> features)
    {
        var kept = new List<GeneratedFeature>(features.Count);
        var seen = new Dictionary<EmissionKey, GeneratedFeature>();
        var identicalDrops = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var feature in features)
        {
            var key = EmissionKeyOf(feature);

            if (key is { } emissionKey)
            {
                if (seen.TryGetValue(emissionKey, out var first) && SameAttributes(first.Attributes, feature.Attributes))
                {
                    identicalDrops[StableIdOf(feature)] = identicalDrops.TryGetValue(StableIdOf(feature), out var count) ? count + 1 : 1;
                    continue;
                }

                seen.TryAdd(emissionKey, feature);
            }

            kept.Add(feature);
        }

        var provenanceGroups = new Dictionary<string, List<GeneratedFeature>>(StringComparer.Ordinal);

        foreach (var feature in kept)
        {
            var stableId = StableIdOf(feature);

            if (!provenanceGroups.TryGetValue(stableId, out var group))
            {
                group = new List<GeneratedFeature>();
                provenanceGroups.Add(stableId, group);
            }

            group.Add(feature);
        }

        var samePartDrops = new Dictionary<string, int>(StringComparer.Ordinal);
        var sourceConflicts = new List<string>();
        var skippedProvenance = new HashSet<GeneratedFeature>();

        foreach (var group in provenanceGroups.Values)
        {
            if (group.Count <= 1 || group.Count(EmitsPlacedFeature) > 1)
            {
                // Groups with two or more placed emitters keep today's behavior: validation
                // rejects the repeated stable id and the conversion fails.
                continue;
            }

            // A provenance-only group merges only when every member is a projection of the
            // same source part with identical source geometry and source attributes; a group
            // that fails that proof must not silently lose a differing member.
            var conflicts = false;

            for (var index = 1; index < group.Count; index++)
            {
                if (!string.Equals(GeometryText(group[0].Geometry), GeometryText(group[index].Geometry), StringComparison.Ordinal)
                    || !SameAttributes(group[0].Attributes, group[index].Attributes))
                {
                    conflicts = true;
                    break;
                }
            }

            if (conflicts)
            {
                sourceConflicts.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.StableIdSourceConflict,
                    $"StableId '{StableIdOf(group[0])}' is shared by {group.Count} features of one source part whose source geometry or source attributes differ; the duplicates cannot be merged."));
                continue;
            }

            for (var index = 1; index < group.Count; index++)
            {
                skippedProvenance.Add(group[index]);
            }

            samePartDrops[StableIdOf(group[0])] = group.Count - 1;
        }

        var provenanceFeatures = new List<GeneratedFeature>(kept.Count);

        foreach (var feature in kept)
        {
            if (!skippedProvenance.Contains(feature))
            {
                provenanceFeatures.Add(feature);
            }
        }

        var diagnostics = new List<string>(identicalDrops.Count + samePartDrops.Count);

        foreach (var pair in identicalDrops)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.StableIdDeduplicated,
                $"StableId '{pair.Key}' was emitted by {pair.Value + 1} identical duplicate features; the duplicates were dropped."));
        }

        foreach (var pair in samePartDrops)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.StableIdDeduplicated,
                $"StableId '{pair.Key}' was emitted by {pair.Value + 1} further features of the same source part; only the first provenance entry was kept."));
        }

        return (kept, provenanceFeatures, diagnostics, sourceConflicts);
    }

    internal static string StableIdOf(GeneratedFeature feature) => $"{feature.FeatureId}/{feature.PartId}";

    private static bool EmitsPlacedFeature(GeneratedFeature feature) =>
        (feature.Kind == GeneratedFeatureKind.ObjectCandidate && feature.Position is not null && feature.ObjectPlacement is not null)
        || (feature.Kind == GeneratedFeatureKind.Waypoint && feature.Position is not null);

    private static EmissionKey? EmissionKeyOf(GeneratedFeature feature)
    {
        var geometry = feature.Geometry switch
        {
            GeoPointGeometry or GeoLineString or GeoPolygon => GeometryText(feature.Geometry),
            _ => null,
        };

        if (geometry is null)
        {
            return null;
        }

        return new EmissionKey(
            StableIdOf(feature),
            feature.Kind,
            feature.RuleIndex,
            feature.SourceId,
            geometry,
            feature.Position,
            feature.ObjectPlacement,
            feature.Passable,
            feature.Buildable,
            feature.Coverage);
    }

    internal static bool SameAttributes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    // Vector layers project every emission from the original payload feature list before
    // provenance dedupe: one source part may legitimately emit several vector features (for
    // example passability plus buildability of one building). Emission identity is the tuple
    // (SourceStableId, Kind, RuleIndex), the same identity the interchange validator checks
    // for duplicates. Exact duplicates collapse to the first emission; any other same-tuple
    // difference is a conflict that fails the conversion, as does a geometry type the vector
    // format cannot represent. Point features are skipped because waypoints and objects
    // already carry them; a point child inside a multi geometry is still converted.
    private static (VectorLayerSet? Layers, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Diagnostics) BuildVectorLayers(
        IReadOnlyList<GeneratedFeature> features)
    {
        var vectorFeatures = new List<VectorFeature>(features.Count);
        var seen = new Dictionary<VectorEmissionKey, VectorEmission>();
        var collapsed = new Dictionary<VectorEmissionKey, int>();
        var conflicts = new List<string>();

        foreach (var feature in features)
        {
            if (feature.Geometry is GeoPointGeometry)
            {
                continue;
            }

            if (ConvertVectorGeometry(feature.Geometry, conflicts) is not { } geometry)
            {
                return (null, conflicts, []);
            }

            var stableId = StableIdOf(feature);
            var key = new VectorEmissionKey(stableId, feature.Kind.ToString(), feature.RuleIndex);

            if (seen.TryGetValue(key, out var first))
            {
                if (string.Equals(first.SourceId, feature.SourceId, StringComparison.Ordinal)
                    && string.Equals(first.GeometryText, VectorGeometryText(feature.Geometry), StringComparison.Ordinal)
                    && SameAttributes(first.Attributes, feature.Attributes)
                    && first.Passable == feature.Passable
                    && first.Buildable == feature.Buildable
                    && first.Coverage == feature.Coverage)
                {
                    collapsed[key] = collapsed.TryGetValue(key, out var count) ? count + 1 : 1;
                    continue;
                }

                conflicts.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.VectorFeatureConflict,
                    $"Vector emission '{stableId}' (kind '{key.Kind}', rule index {key.RuleIndex}) is emitted again with a different SourceId, geometry, attributes, or flags; the conflicting vector emissions cannot be merged."));
                return (null, conflicts, []);
            }

            seen.Add(key, new VectorEmission(
                feature.SourceId,
                VectorGeometryText(feature.Geometry),
                feature.Attributes,
                feature.Passable,
                feature.Buildable,
                feature.Coverage));
            vectorFeatures.Add(new VectorFeature(
                stableId,
                feature.SourceId,
                key.Kind,
                feature.RuleIndex,
                feature.Attributes,
                feature.Passable,
                feature.Buildable,
                feature.Coverage?.ToString(),
                geometry));
        }

        if (vectorFeatures.Count == 0)
        {
            return (null, conflicts, []);
        }

        var diagnostics = new List<string>(collapsed.Count);

        foreach (var pair in collapsed)
        {
            diagnostics.Add(Format(
                GeneratedMapPackageDiagnosticCodes.StableIdDeduplicated,
                $"Vector emission '{pair.Key.SourceStableId}' (kind '{pair.Key.Kind}', rule index {pair.Key.RuleIndex}) was emitted by {pair.Value + 1} identical duplicate features; the identical duplicate vector emissions were collapsed."));
        }

        return (new VectorLayerSet(vectorFeatures), conflicts, diagnostics);
    }

    internal static VectorGeometry? ConvertVectorGeometry(GeoGeometry geometry, List<string> conflicts)
    {
        switch (geometry)
        {
            case GeoPointGeometry point:
                return new VectorPointGeometry(ConvertVectorPosition(point.Position));
            case GeoLineString line:
                return new VectorLineStringGeometry(line.Positions.Select(ConvertVectorPosition).ToArray());
            case GeoPolygon polygon:
                return new VectorPolygonGeometry(
                    ConvertVectorRing(polygon.OuterRing),
                    polygon.InnerRings.Select(ConvertVectorRing).ToArray());
            case GeoMultiGeometry multi:
                return ConvertVectorMulti(multi, conflicts);
            default:
                conflicts.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.VectorFeatureConflict,
                    $"Feature geometry type '{geometry.GetType().Name}' is not a supported vector geometry; the conversion cannot represent it."));
                return null;
        }
    }

    private static VectorMultiGeometry? ConvertVectorMulti(GeoMultiGeometry multi, List<string> conflicts)
    {
        var children = new List<VectorGeometry>(multi.Geometries.Count);

        foreach (var child in multi.Geometries)
        {
            if (ConvertVectorGeometry(child, conflicts) is not { } converted)
            {
                return null;
            }

            children.Add(converted);
        }

        return new VectorMultiGeometry(children);
    }

    private static VectorRing ConvertVectorRing(GeoRing ring) =>
        new(ring.Positions.Select(ConvertVectorPosition).ToArray());

    private static VectorPosition ConvertVectorPosition(GeoPosition position) =>
        new(position.Coordinate.X, position.Coordinate.Y, position.AltitudeMeters);

    // Vector identity comparison text. GeometryText renders a multi geometry as the bare
    // type name, which would make distinct same-tuple multis look identical and collapse
    // instead of conflicting, so multis render their children recursively. Line, polygon,
    // and point texts stay identical to GeometryText.
    internal static string VectorGeometryText(GeoGeometry geometry) => geometry switch
    {
        GeoMultiGeometry multi => "Multi(" + string.Join(", ", multi.Geometries.Select(VectorGeometryText)) + ")",
        _ => GeometryText(geometry),
    };

    private static PlacedObjectSet? BuildObjects(IReadOnlyList<GeneratedFeature> features, IReadOnlyList<PlacedObject> skirmishWaypoints)
    {
        var objects = new List<PlacedObject>();

        foreach (var feature in features)
        {
            if (feature.Kind != GeneratedFeatureKind.ObjectCandidate
                || feature.Position is not { } position
                || feature.ObjectPlacement is not { } placement)
            {
                continue;
            }

            objects.Add(new PlacedObject(
                feature.FeatureId,
                GameXy(position.X),
                GameXy(position.Y),
                GameZ(position.Z),
                placement.OrientationDegrees * Math.PI / 180d,
                new AssetReference(LegacyAssetScope, placement.ObjectDefinition),
                0u,
                new Dictionary<string, MapPropertyValue>()));
        }

        objects.AddRange(skirmishWaypoints);

        return objects.Count == 0 ? null : new PlacedObjectSet(objects);
    }

    // The engine's player starts are MapObjects with the starred waypoint type name whose
    // waypointID/waypointName properties the runtime waypoint scanner reads (Waypoint.CreateWaypointMapObject,
    // Scripting/Waypoints.cs); Scene3D resolves Player_{N}_Start 1-based. Z is sampled from the
    // quantized chunk heights this converter writes, the same heights the resident height map carries.
    private static bool TryBuildSkirmishStarts(
        IReadOnlyList<GeneratedMapSkirmishSlot> slots,
        IReadOnlyList<Chunk> chunks,
        int latticeWidth,
        int latticeHeight,
        List<PlacedObject> waypoints,
        List<string> factions,
        List<string> diagnostics)
    {
        var extentX = (double)(latticeWidth - 1) * ChunkLayout.SampleSpacingGameUnits;
        var extentY = (double)(latticeHeight - 1) * ChunkLayout.SampleSpacingGameUnits;
        var seen = new HashSet<(double X, double Y)>();

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            var faction = slot.FactionTemplateName;

            if (!GeneratedMapSkirmishConfiguration.FactionTemplateNames.Contains(faction))
            {
                diagnostics.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration,
                    $"Skirmish slot {index + 1} declares faction '{faction}', which is not a known player template; known factions: {string.Join(", ", GeneratedMapSkirmishConfiguration.FactionTemplateNames)}; no package was written."));
                return false;
            }

            if (!double.IsFinite(slot.X) || !double.IsFinite(slot.Y) || slot.X < 0 || slot.Y < 0 || slot.X > extentX || slot.Y > extentY)
            {
                diagnostics.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration,
                    $"Skirmish slot {index + 1} declares the start position ({CoordinateText(slot.X)}, {CoordinateText(slot.Y)}), which is outside the map extent 0..{CoordinateText(extentX)} x 0..{CoordinateText(extentY)} game units; no package was written."));
                return false;
            }

            if (!seen.Add((slot.X, slot.Y)))
            {
                diagnostics.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration,
                    $"Skirmish slot {index + 1} repeats the start position ({CoordinateText(slot.X)}, {CoordinateText(slot.Y)}) of an earlier slot; every slot needs its own start position; no package was written."));
                return false;
            }

            var name = $"Player_{index + 1}_Start";
            waypoints.Add(new PlacedObject(
                name,
                GameXy(slot.X),
                GameXy(slot.Y),
                GameZ(SampleLatticeHeight(chunks, latticeWidth, latticeHeight, slot.X, slot.Y)),
                0d,
                new AssetReference(LegacyAssetScope, Panopticon.Scripting.Waypoint.ObjectTypeName),
                0u,
                new Dictionary<string, MapPropertyValue>
                {
                    ["waypointID"] = new MapPropertyValue.IntegerValue(index + 1),
                    ["waypointName"] = new MapPropertyValue.AsciiStringValue(name),
                }));
            factions.Add(faction);
        }

        return true;
    }

    private static float SampleLatticeHeight(IReadOnlyList<Chunk> chunks, int latticeWidth, int latticeHeight, double x, double y)
    {
        var latticeX = Math.Clamp((int)Math.Round(x / ChunkLayout.SampleSpacingGameUnits), 0, latticeWidth - 1);
        var latticeY = Math.Clamp((int)Math.Round(y / ChunkLayout.SampleSpacingGameUnits), 0, latticeHeight - 1);
        var cx = latticeX / ChunkLayout.IntervalsPerChunk;
        var cy = latticeY / ChunkLayout.IntervalsPerChunk;
        var chunk = chunks.Single(candidate => candidate.Key.Cx == cx && candidate.Key.Cy == cy);
        var originX = cx * ChunkLayout.IntervalsPerChunk;
        var originY = cy * ChunkLayout.IntervalsPerChunk;
        var coreWidth = Math.Min(ChunkLayout.PointsPerChunkAxis, latticeWidth - originX);
        var column = latticeX - originX + 1;
        var row = latticeY - originY + 1;

        return chunk.Heights.Samples[(row * (coreWidth + 2)) + column];
    }

    private static string StartListText(IReadOnlyList<PlacedObject> waypoints) =>
        string.Join(", ", waypoints.Select(waypoint => $"{waypoint.StableId} ({CoordinateText(waypoint.X.Value)}, {CoordinateText(waypoint.Y.Value)})"));

    private static string CoordinateText(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static WaypointSet? BuildWaypoints(IReadOnlyList<GeneratedFeature> features)
    {
        var waypoints = new List<Waypoint>();

        foreach (var feature in features)
        {
            if (feature.Kind != GeneratedFeatureKind.Waypoint || feature.Position is not { } position)
            {
                continue;
            }

            waypoints.Add(new Waypoint(
                feature.FeatureId,
                WaypointName(feature.Attributes, feature.FeatureId),
                GameXy(position.X),
                GameXy(position.Y),
                GameZ(position.Z)));
        }

        return waypoints.Count == 0 ? null : new WaypointSet(waypoints);
    }

    private static string WaypointName(IReadOnlyDictionary<string, string> attributes, string featureId) =>
        attributes.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : featureId;

    private static Provenance BuildProvenance(GeneratedMapPayloadMetadata metadata, IReadOnlyList<GeneratedFeature> emittedFeatures)
    {
        var sources = metadata.Provenance
            .Select(source => new SourceRecord(source.Kind.ToString(), [source.Sha256Hex], null, null))
            .ToArray();

        var features = new List<MapGeneratedFeature>(emittedFeatures.Count);

        foreach (var feature in emittedFeatures)
        {
            var attributes = new Dictionary<string, string>(feature.Attributes, StringComparer.Ordinal)
            {
                ["sourceId"] = feature.SourceId,
                ["ruleIndex"] = feature.RuleIndex.ToString(CultureInfo.InvariantCulture),
                ["kind"] = feature.Kind.ToString(),
                ["geometry"] = GeometryText(feature.Geometry),
            };

            features.Add(new MapGeneratedFeature(
                $"{feature.FeatureId}/{feature.PartId}",
                feature.Kind.ToString(),
                attributes));
        }

        return new Provenance(metadata.SchemaVersion.ToString(CultureInfo.InvariantCulture), sources, features);
    }

    private static ImportReport BuildImportReport(GeneratedMapPayloadReport report)
    {
        var sampling = report.Terrain.Sampling;

        return new ImportReport(
            report.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Severity}: {diagnostic.Message}").ToArray(),
            GsdRatioTexts(sampling),
            (int)sampling.OutsideFillCount,
            (int)sampling.NoDataFillCount,
            report.ValidationChecks
                .Where(check => check.Category == ControlPointCategory)
                .Select(FormatCheck)
                .ToArray());
    }

    private static IReadOnlyList<string> GsdRatioTexts(PayloadSamplingSummary sampling)
    {
        var texts = new List<string>();
        var ranges = sampling.ObservedRanges;

        if (ranges?.GsdRatioX is { } ratioX)
        {
            texts.Add(RangeText("GsdRatioX", ratioX));
        }

        if (ranges?.GsdRatioY is { } ratioY)
        {
            texts.Add(RangeText("GsdRatioY", ratioY));
        }

        return texts;
    }

    private static string RangeText(string name, PayloadObservedRange range) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} [{range.Min}, {range.Max}]");

    private static string FormatCheck(PayloadValidationCheck check) =>
        $"{check.Name}: {check.Status}" + (check.Message is null ? string.Empty : $": {check.Message}");

    internal static string GeometryText(GeoGeometry geometry) => geometry switch
    {
        GeoPointGeometry point => $"Point({PositionText(point.Position)})",
        GeoLineString line => "LineString(" + string.Join(", ", line.Positions.Select(PositionText)) + ")",
        GeoPolygon polygon => "Polygon(" + string.Join(", ", new[] { polygon.OuterRing }.Concat(polygon.InnerRings).Select(RingText)) + ")",
        _ => geometry.GetType().Name,
    };

    internal static string RingText(GeoRing ring) => "(" + string.Join(", ", ring.Positions.Select(PositionText)) + ")";

    internal static string PositionText(GeoPosition position)
    {
        var coordinate = position.Coordinate;

        return position.AltitudeMeters is { } altitude
            ? string.Create(CultureInfo.InvariantCulture, $"{coordinate.X} {coordinate.Y} {altitude}")
            : string.Create(CultureInfo.InvariantCulture, $"{coordinate.X} {coordinate.Y}");
    }

    private static MapQuantity GameXy(double value) => new(value, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy);

    private static MapQuantity GameZ(double value) => new(value, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ);

    // Everything the converter projects into the interchange except the attribute text, which
    // is compared separately because dictionaries have no structural equality. Geometries with
    // no exact emitted text (composite geometries emit only the type name) return null so
    // those features never group as duplicates.
    private readonly record struct EmissionKey(
        string StableId,
        GeneratedFeatureKind Kind,
        int RuleIndex,
        string SourceId,
        string Geometry,
        GamePosition? Position,
        GeneratedObjectPlacement? Placement,
        bool? Passable,
        bool? Buildable,
        ManifestMaskCoverage? Coverage);

    // Vector emission identity; the interchange validator checks the same tuple for duplicates.
    private readonly record struct VectorEmissionKey(string SourceStableId, string Kind, int RuleIndex);

    // First emission for a vector key; geometry is compared as emitted text and attributes
    // separately because dictionaries have no structural equality.
    private readonly record struct VectorEmission(
        string SourceId,
        string GeometryText,
        IReadOnlyDictionary<string, string> Attributes,
        bool? Passable,
        bool? Buildable,
        ManifestMaskCoverage? Coverage);

    private static string Format(string code, string message) => $"{code}: {message}";

    private static GeneratedMapPackageResult Failed(IEnumerable<string> diagnostics) =>
        new(false, diagnostics.ToArray(), null, null, 0, []);
}

/// <summary>One operator-declared skirmish slot: the faction player template name and the map-local start position in game units.</summary>
public sealed record GeneratedMapSkirmishSlot(string FactionTemplateName, double X, double Y);

/// <summary>
/// Parser for the package verb's skirmish opt-in (<c>"FactionAmerica:100,100;FactionGLA:300,300"</c>).
/// Slots are 1-based by declaration order: slot N becomes the player start waypoint
/// <c>Player_{N}_Start</c> for the Nth listed faction. Syntax, faction names, coordinate
/// signs, and duplicate start positions fail here with one actionable message; the
/// beyond-map bound is enforced by the converter, which owns the terrain extent.
/// </summary>
public static class GeneratedMapSkirmishConfiguration
{
    /// <summary>The base-faction PlayerTemplate block names of C&amp;C Generals; skirmish players are looked up by these names.</summary>
    public static readonly IReadOnlyList<string> FactionTemplateNames = ["FactionAmerica", "FactionChina", "FactionGLA"];

    public static bool TryParse(string? text, out IReadOnlyList<GeneratedMapSkirmishSlot> slots, [NotNullWhen(false)] out string? error)
    {
        slots = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = EmptyMessage;
            return false;
        }

        var parsed = new List<GeneratedMapSkirmishSlot>();
        var seen = new HashSet<(double X, double Y)>();

        foreach (var rawSlot in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawSlot.IndexOf(':');

            if (separator <= 0)
            {
                error = $"the skirmish slot '{rawSlot}' is not faction:x,y; declare slots as faction:x,y separated by semicolons (for example {Example})";
                return false;
            }

            var faction = rawSlot[..separator].Trim();

            if (!FactionTemplateNames.Contains(faction))
            {
                error = $"the skirmish slot '{rawSlot}' declares faction '{faction}', which is not a known player template; known factions: {string.Join(", ", FactionTemplateNames)}";
                return false;
            }

            var coordinates = rawSlot[(separator + 1)..].Split(',');

            if (coordinates.Length != 2
                || !double.TryParse(coordinates[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(coordinates[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                error = $"the skirmish slot '{rawSlot}' does not carry two finite invariant-culture game-unit coordinates; declare slots as faction:x,y (for example {Example})";
                return false;
            }

            if (x < 0 || y < 0)
            {
                error = $"the skirmish slot '{rawSlot}' declares a negative coordinate ({FormatValue(x)}, {FormatValue(y)}); start positions are non-negative game units";
                return false;
            }

            if (!seen.Add((x, y)))
            {
                error = $"the skirmish slot '{rawSlot}' repeats the start position ({FormatValue(x)}, {FormatValue(y)}) of an earlier slot; every slot needs its own start position";
                return false;
            }

            parsed.Add(new GeneratedMapSkirmishSlot(faction, x, y));
        }

        if (parsed.Count == 0)
        {
            error = EmptyMessage;
            return false;
        }

        slots = parsed;
        return true;
    }

    private const string Example = "FactionAmerica:100,100;FactionGLA:300,300";

    private const string EmptyMessage = "the skirmish configuration is empty; declare at least one slot as faction:x,y (for example " + Example + ")";

    private static string FormatValue(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
