using System;
using System.Collections.Generic;
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
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using MapGeneratedFeature = Panopticon.Content.Importing.Maps.GeneratedFeature;

namespace Panopticon.Geospatial.Preview;

public static class GeneratedMapPackageConverter
{
    private const string TerrainAssetScope = "terrain";
    private const string LegacyAssetScope = "legacy";
    private const string ControlPointCategory = "control-point";
    private const int HeightSectionVersion = 5;

    private static readonly DecoderOptions DecodeOptions = new() { SkipMetadata = true };

    private static readonly IReadOnlyList<string> Limitations =
    [
        "Palette textures are material names in the terrain asset scope; gameplay texture binding is not performed.",
        "Imagery keeps payload PNG row order (north row first).",
        "Line and polygon feature geometry is preserved as provenance attribute text, not package layers.",
        "Conversion holds chunk data in memory; streaming is not supported.",
    ];

    public static GeneratedMapPackageResult Convert(IGeneratedMapPayloadReader reader, string mapName, string targetPackageRoot)
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

        for (var chunkY = 0; chunkY < grid.ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < grid.ChunksX; chunkX++)
            {
                var chunk = ReadChunk(reader, grid.ImageryPixelsPerSample is not null, chunkX, chunkY, diagnostics);

                if (chunk is null)
                {
                    return Failed(diagnostics);
                }

                chunks.Add(chunk);
            }
        }

        var interchange = new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity(mapName, MapSourceKind.Generated, null),
            BuildLayout(grid, chunks, latticeWidth, latticeHeight, (uint)mapArea),
            BuildBlend(metadata),
            BuildObjects(metadata),
            BuildWaypoints(metadata),
            BuildProvenance(metadata),
            BuildImportReport(payloadReport),
            []);

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

        return new GeneratedMapPackageResult(true, [], interchange, targetPackageRoot, chunks.Count, Limitations);
    }

    private static Chunk? ReadChunk(
        IGeneratedMapPayloadReader reader,
        bool hasImagery,
        int chunkX,
        int chunkY,
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
            new HeightLayer(terrain.Heights.ToArray()),
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

    private static PlacedObjectSet? BuildObjects(GeneratedMapPayloadMetadata metadata)
    {
        var objects = new List<PlacedObject>();

        foreach (var feature in metadata.Features)
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

        return objects.Count == 0 ? null : new PlacedObjectSet(objects);
    }

    private static WaypointSet? BuildWaypoints(GeneratedMapPayloadMetadata metadata)
    {
        var waypoints = new List<Waypoint>();

        foreach (var feature in metadata.Features)
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

    private static Provenance BuildProvenance(GeneratedMapPayloadMetadata metadata)
    {
        var sources = metadata.Provenance
            .Select(source => new SourceRecord(source.Kind.ToString(), [source.Sha256Hex], null, null))
            .ToArray();

        var features = new List<MapGeneratedFeature>(metadata.Features.Count);

        foreach (var feature in metadata.Features)
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

    private static string GeometryText(GeoGeometry geometry) => geometry switch
    {
        GeoPointGeometry point => $"Point({PositionText(point.Position)})",
        GeoLineString line => "LineString(" + string.Join(", ", line.Positions.Select(PositionText)) + ")",
        GeoPolygon polygon => "Polygon(" + string.Join(", ", new[] { polygon.OuterRing }.Concat(polygon.InnerRings).Select(RingText)) + ")",
        _ => geometry.GetType().Name,
    };

    private static string RingText(GeoRing ring) => "(" + string.Join(", ", ring.Positions.Select(PositionText)) + ")";

    private static string PositionText(GeoPosition position)
    {
        var coordinate = position.Coordinate;

        return position.AltitudeMeters is { } altitude
            ? string.Create(CultureInfo.InvariantCulture, $"{coordinate.X} {coordinate.Y} {altitude}")
            : string.Create(CultureInfo.InvariantCulture, $"{coordinate.X} {coordinate.Y}");
    }

    private static MapQuantity GameXy(double value) => new(value, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy);

    private static MapQuantity GameZ(double value) => new(value, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ);

    private static string Format(string code, string message) => $"{code}: {message}";

    private static GeneratedMapPackageResult Failed(IEnumerable<string> diagnostics) =>
        new(false, diagnostics.ToArray(), null, null, 0, []);
}
