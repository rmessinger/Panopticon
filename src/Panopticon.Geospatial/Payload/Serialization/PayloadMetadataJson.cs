using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Explicit conversion between the internal metadata DTOs and the immutable payload
/// models, plus the deterministic streaming JSON writer for <c>payload.json</c>.
/// Conversion revalidates every value by constructing models through their validating
/// constructors; shared attribute tables convert to one immutable dictionary per table
/// that all referencing features share.
/// </summary>
internal static class PayloadMetadataJson
{
    private const int PositionsPerFlush = 128;

    internal static GeneratedMapPayloadMetadata Build(PayloadMetadataDto dto)
    {
        var kind = RequireString(dto.Kind, "kind");
        if (kind != GeneratedMapPayloadMetadata.PayloadKind)
        {
            throw Schema($"The payload kind '{kind}' is not '{GeneratedMapPayloadMetadata.PayloadKind}'.");
        }

        var schemaVersion = Require(dto.SchemaVersion, "schemaVersion");
        if (schemaVersion != GeneratedMapPayloadMetadata.CurrentSchemaVersion)
        {
            throw Schema(
                $"The schema version {schemaVersion} is unsupported; this reader supports version {GeneratedMapPayloadMetadata.CurrentSchemaVersion}.");
        }

        var grid = BuildGrid(Require(dto.Grid, "grid"));
        var palette = new MaterialPalette(Require(dto.Palette, "palette"));
        var features = BuildFeatures(Require(dto.Features, "features"));
        var provenance = BuildProvenance(Require(dto.Provenance, "provenance"));
        var chunks = BuildChunks(Require(dto.Chunks, "chunks"));

        return new GeneratedMapPayloadMetadata(
            grid,
            palette,
            features,
            RequireString(dto.ManifestSha256, "manifestSha256"),
            provenance,
            chunks);
    }

    internal static void Write(Utf8JsonWriter writer, GeneratedMapPayloadMetadata metadata, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", metadata.Kind);
        writer.WriteNumber("schemaVersion", metadata.SchemaVersion);

        WriteGrid(writer, metadata.Grid, context);
        WritePalette(writer, metadata.Palette, context);
        WriteFeatures(writer, metadata.Features, context);

        context.EnsureStringWithinBudget(writer, metadata.ManifestSha256Hex);
        writer.WriteString("manifestSha256", metadata.ManifestSha256Hex);

        WriteProvenance(writer, metadata.Provenance, context);
        WriteChunks(writer, metadata.Chunks, context);

        writer.WriteEndObject();
    }

    private static PayloadGridDefinition BuildGrid(PayloadGridDto grid)
    {
        var chunksX = Require(grid.ChunksX, "grid.chunksX");
        var chunksY = Require(grid.ChunksY, "grid.chunksY");
        if (chunksX < 1 || chunksY < 1)
        {
            throw Schema("grid.chunksX and grid.chunksY must be positive.");
        }

        ValidateLayout(Require(grid.Layout, "grid.layout"));

        var transformDto = Require(grid.Transform, "grid.transform");
        var sourceSystems = new List<CoordinateSystemId>();

        foreach (var crs in Require(transformDto.SourceCoordinateSystems, "grid.transform.sourceCoordinateSystems"))
        {
            sourceSystems.Add(BuildCrs(crs, "grid.transform.sourceCoordinateSystems[]"));
        }

        var transform = new GeospatialMapTransform(
            sourceSystems,
            BuildCrs(Require(transformDto.ProjectedCoordinateSystem, "grid.transform.projectedCoordinateSystem"), "grid.transform.projectedCoordinateSystem"),
            new Vector2d(
                Require(transformDto.OriginEasting, "grid.transform.originEastingNorthing.x"),
                Require(transformDto.OriginNorthing, "grid.transform.originEastingNorthing.y")),
            Require(transformDto.MetersPerGameUnit, "grid.transform.metersPerGameUnit"),
            RequireString(transformDto.VerticalDatum, "grid.transform.verticalDatum"),
            Require(transformDto.VerticalOriginMeters, "grid.transform.verticalOriginMeters"),
            Require(transformDto.VerticalExaggeration, "grid.transform.verticalExaggeration"));

        var playableArea = BuildPolygon(Require(grid.PlayableArea, "grid.playableArea"));
        var thresholds = new SlopeThresholds(
            Require(grid.ImpassableSlopeDegrees, "grid.slopeThresholds.impassableDegrees"),
            Require(grid.UnbuildableSlopeDegrees, "grid.slopeThresholds.unbuildableDegrees"));

        return new PayloadGridDefinition(chunksX, chunksY, transform, playableArea, grid.ImageryPixelsPerSample, thresholds);
    }

    private static void ValidateLayout(PayloadLayoutDto layout)
    {
        if (layout.SampleSpacingGameUnits != ChunkLayout.SampleSpacingGameUnits
            || layout.IntervalsPerChunk != ChunkLayout.IntervalsPerChunk
            || layout.PointsPerChunkAxis != ChunkLayout.PointsPerChunkAxis
            || layout.ApronSamplesPerSide != ChunkLayout.ApronSamplesPerSide
            || layout.StoredPointsPerChunkAxis != ChunkLayout.StoredPointsPerChunkAxis)
        {
            throw Schema(
                $"The chunk layout {layout.SampleSpacingGameUnits}/{layout.IntervalsPerChunk}/{layout.PointsPerChunkAxis}/{layout.ApronSamplesPerSide}/{layout.StoredPointsPerChunkAxis} "
                + $"is unsupported; this reader supports the fixed {ChunkLayout.SampleSpacingGameUnits}/{ChunkLayout.IntervalsPerChunk}/{ChunkLayout.PointsPerChunkAxis}/{ChunkLayout.ApronSamplesPerSide}/{ChunkLayout.StoredPointsPerChunkAxis} lattice.");
        }
    }

    private static CoordinateSystemId BuildCrs(PayloadCrsDto? crs, string member)
    {
        if (crs is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        if (crs.EpsgCode is { } epsgCode)
        {
            if (crs.CentralMeridianDegrees is not null || crs.Scale is not null
                || crs.FalseEasting is not null || crs.FalseNorthing is not null)
            {
                throw Schema($"The coordinate system '{member}' mixes 'epsg' with Transverse Mercator members.");
            }

            return CoordinateSystemId.FromEpsg(epsgCode);
        }

        return CoordinateSystemId.TransverseMercator(
            Require(crs.CentralMeridianDegrees, member + ".centralMeridianDegrees"),
            Require(crs.Scale, member + ".scale"),
            Require(crs.FalseEasting, member + ".falseEasting"),
            Require(crs.FalseNorthing, member + ".falseNorthing"));
    }

    private static GeoPolygon BuildPolygon(PayloadPolygonDto polygon)
    {
        return new GeoPolygon(
            BuildRing(Require(polygon.OuterRing, "polygon.outerRing")),
            BuildRings(polygon.InnerRings));
    }

    private static List<GeoRing> BuildRings(List<PayloadRingDto>? rings)
    {
        var result = new List<GeoRing>(rings?.Count ?? 0);

        foreach (var ring in rings ?? [])
        {
            result.Add(BuildRing(ring));
        }

        return result;
    }

    private static GeoRing BuildRing(PayloadRingDto ring)
    {
        return new GeoRing(BuildPositions(Require(ring.Positions, "ring.positions")));
    }

    private static List<GeoRing> BuildFeatureRings(List<PayloadRingDto>? rings, string member)
    {
        var result = new List<GeoRing>(rings?.Count ?? 0);

        for (var index = 0; index < (rings?.Count ?? 0); index++)
        {
            result.Add(BuildFeatureRing(rings![index], $"{member}[{index}]"));
        }

        return result;
    }

    /// <summary>
    /// Builds one feature polygon ring, rejecting unclosed, degenerate, or non-finite
    /// rings as value errors through the shared ring validator.
    /// </summary>
    private static GeoRing BuildFeatureRing(PayloadRingDto? ring, string member)
    {
        if (ring is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        var positions = BuildPositions(Require(ring.Positions, member + ".positions"));

        PayloadValidationRules.RingPositions(positions, member, "geometry");

        return new GeoRing(positions);
    }

    private static GeoGeometry BuildGeometry(PayloadGeometryDto geometry)
    {
        switch (geometry.Type)
        {
            case "point":
                RejectGeometryMembers(geometry, positions: true, outerRing: true, innerRings: true, geometries: true);
                return new GeoPointGeometry(BuildPosition(Require(geometry.Position, "geometry.position")));
            case "line-string":
                RejectGeometryMembers(geometry, position: true, outerRing: true, innerRings: true, geometries: true);
                return new GeoLineString(BuildPositions(Require(geometry.Positions, "geometry.positions")));
            case "polygon":
                RejectGeometryMembers(geometry, position: true, positions: true, geometries: true);
                return new GeoPolygon(
                    BuildFeatureRing(geometry.OuterRing, "geometry.outerRing"),
                    BuildFeatureRings(geometry.InnerRings, "geometry.innerRings"));
            case "multi":
                RejectGeometryMembers(geometry, position: true, positions: true, outerRing: true, innerRings: true);
                var geometries = new List<GeoGeometry>();

                foreach (var nested in Require(geometry.Geometries, "geometry.geometries"))
                {
                    geometries.Add(BuildGeometry(nested));
                }

                return new GeoMultiGeometry(geometries);
            case null:
                throw Schema("The member 'geometry.type' is required.");
            default:
                throw Schema($"Unknown geometry type '{geometry.Type}'.");
        }
    }

    private static void RejectGeometryMembers(
        PayloadGeometryDto geometry,
        bool position = false,
        bool positions = false,
        bool outerRing = false,
        bool innerRings = false,
        bool geometries = false)
    {
        if (position && geometry.Position is not null)
        {
            throw Schema($"A '{geometry.Type}' geometry must not carry 'position'.");
        }

        if (positions && geometry.Positions is not null)
        {
            throw Schema($"A '{geometry.Type}' geometry must not carry 'positions'.");
        }

        if (outerRing && geometry.OuterRing is not null)
        {
            throw Schema($"A '{geometry.Type}' geometry must not carry 'outerRing'.");
        }

        if (innerRings && geometry.InnerRings is not null)
        {
            throw Schema($"A '{geometry.Type}' geometry must not carry 'innerRings'.");
        }

        if (geometries && geometry.Geometries is not null)
        {
            throw Schema($"A '{geometry.Type}' geometry must not carry 'geometries'.");
        }
    }

    private static GeoPosition BuildPosition(PayloadPositionDto position)
    {
        return new GeoPosition(new Vector2d(position.X, position.Y), position.AltitudeMeters);
    }

    private static GeoPosition[] BuildPositions(List<PayloadPositionDto> positions)
    {
        var result = new GeoPosition[positions.Count];

        for (var index = 0; index < positions.Count; index++)
        {
            result[index] = BuildPosition(positions[index]);
        }

        return result;
    }

    private static List<GeneratedFeature> BuildFeatures(PayloadFeaturesDto features)
    {
        var tablesDto = Require(features.AttributeTables, "features.attributeTables");
        var tables = new IReadOnlyDictionary<string, string>[tablesDto.Count];

        for (var index = 0; index < tablesDto.Count; index++)
        {
            tables[index] = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(tablesDto[index].Entries, StringComparer.Ordinal));
        }

        var items = Require(features.Items, "features.items");
        var result = new List<GeneratedFeature>(items.Count);

        foreach (var item in items)
        {
            result.Add(BuildFeature(item, tables));
        }

        return result;
    }

    private static GeneratedFeature BuildFeature(PayloadFeatureDto feature, IReadOnlyDictionary<string, string>[] tables)
    {
        var kind = ParseEnum<GeneratedFeatureKind>(feature.Kind, "feature.kind");
        var geometry = BuildGeometry(Require(feature.Geometry, "feature.geometry"));

        IReadOnlyDictionary<string, string>? attributes = null;
        if (feature.AttributeTable is { } tableIndex)
        {
            if (tableIndex < 0 || tableIndex >= tables.Length)
            {
                throw Schema($"The feature '{feature.FeatureId}' references attribute table {tableIndex}, which does not exist.");
            }

            attributes = tables[tableIndex];
        }

        GamePosition? position = null;
        if (feature.PositionX is not null || feature.PositionY is not null || feature.PositionZ is not null)
        {
            position = new GamePosition(
                Require(feature.PositionX, "feature.position.x"),
                Require(feature.PositionY, "feature.position.y"),
                Require(feature.PositionZ, "feature.position.z"));
        }

        GeneratedObjectPlacement? placement = null;
        if (feature.ObjectDefinition is not null || feature.OrientationDegrees is not null)
        {
            placement = new GeneratedObjectPlacement(
                RequireString(feature.ObjectDefinition, "feature.objectPlacement.objectDefinition"),
                Require(feature.OrientationDegrees, "feature.objectPlacement.orientationDegrees"));
        }

        return GeneratedFeature.CreateShared(
            kind,
            geometry,
            RequireString(feature.SourceId, "feature.sourceId"),
            RequireString(feature.FeatureId, "feature.featureId"),
            RequireString(feature.PartId, "feature.partId"),
            Require(feature.RuleIndex, "feature.ruleIndex"),
            attributes,
            position,
            placement,
            feature.Passable,
            feature.Buildable);
    }

    private static List<PayloadSourceProvenance> BuildProvenance(List<PayloadSourceProvenanceDto> provenance)
    {
        var result = new List<PayloadSourceProvenance>(provenance.Count);

        foreach (var source in provenance)
        {
            result.Add(
                new PayloadSourceProvenance(
                    RequireString(source.SourceId, "provenance.sourceId"),
                    ParseEnum<ManifestSourceKind>(source.Kind, "provenance.kind"),
                    ParseEnum<ManifestSourceRole>(source.Role, "provenance.role"),
                    RequireString(source.RelativePath, "provenance.relativePath"),
                    RequireString(source.Sha256, "provenance.sha256"),
                    Require(source.LengthBytes, "provenance.lengthBytes"),
                    BuildCrs(Require(source.ActualCoordinateSystem, "provenance.actualCoordinateSystem"), "provenance.actualCoordinateSystem"),
                    RequireString(source.DeclaredVerticalDatum, "provenance.declaredVerticalDatum"),
                    source.EmbeddedVerticalDatum,
                    source.Label,
                    source.Attribution,
                    source.HorizontalAccuracyMeters,
                    source.VerticalAccuracyMeters));
        }

        return result;
    }

    private static List<PayloadChunkDescriptor> BuildChunks(List<PayloadChunkDto> chunks)
    {
        var result = new List<PayloadChunkDescriptor>(chunks.Count);

        foreach (var chunk in chunks)
        {
            result.Add(
                new PayloadChunkDescriptor(
                    Require(chunk.ChunkX, "chunk.cx"),
                    Require(chunk.ChunkY, "chunk.cy"),
                    BuildFileReference(Require(chunk.Terrain, "chunk.terrain"), "chunk.terrain"),
                    BuildFileReference(Require(chunk.Materials, "chunk.materials"), "chunk.materials"),
                    BuildFileReference(Require(chunk.Masks, "chunk.masks"), "chunk.masks"),
                    chunk.Imagery is null ? null : BuildFileReference(chunk.Imagery, "chunk.imagery")));
        }

        return result;
    }

    private static PayloadFileReference BuildFileReference(PayloadFileReferenceDto reference, string member)
    {
        return new PayloadFileReference(
            RequireString(reference.Path, member + ".path"),
            Require(reference.LengthBytes, member + ".lengthBytes"),
            RequireString(reference.Sha256, member + ".sha256"));
    }

    private static T ParseEnum<T>(string? token, string member)
        where T : struct, Enum
    {
        if (token is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        if (!PayloadEnumTokens<T>.TryGetValue(token, out var value))
        {
            throw Schema($"Unknown {typeof(T).Name} token '{token}' in '{member}'.");
        }

        return value;
    }

    private static T Require<T>(T? value, string member)
        where T : struct
    {
        if (value is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        return value.Value;
    }

    private static T Require<T>(T? value, string member)
        where T : class
    {
        if (value is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        return value;
    }

    private static string RequireString(string? value, string member)
    {
        if (value is null)
        {
            throw Schema($"The member '{member}' is required.");
        }

        return value;
    }

    private static PayloadJsonSchemaException Schema(string message)
    {
        return new PayloadJsonSchemaException(message);
    }

    private static void WriteGrid(Utf8JsonWriter writer, PayloadGridDefinition grid, PayloadWriteContext context)
    {
        writer.WritePropertyName("grid");
        writer.WriteStartObject();

        writer.WriteNumber("chunksX", grid.ChunksX);
        writer.WriteNumber("chunksY", grid.ChunksY);

        writer.WritePropertyName("layout");
        writer.WriteStartObject();
        writer.WriteNumber("sampleSpacingGameUnits", ChunkLayout.SampleSpacingGameUnits);
        writer.WriteNumber("intervalsPerChunk", ChunkLayout.IntervalsPerChunk);
        writer.WriteNumber("pointsPerChunkAxis", ChunkLayout.PointsPerChunkAxis);
        writer.WriteNumber("apronSamplesPerSide", ChunkLayout.ApronSamplesPerSide);
        writer.WriteNumber("storedPointsPerChunkAxis", ChunkLayout.StoredPointsPerChunkAxis);
        writer.WriteEndObject();

        var transform = grid.Transform;
        writer.WritePropertyName("transform");
        writer.WriteStartObject();

        writer.WritePropertyName("sourceCoordinateSystems");
        writer.WriteStartArray();

        foreach (var source in transform.SourceCoordinateSystems)
        {
            WriteCrs(writer, source);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("projectedCoordinateSystem");
        WriteCrs(writer, transform.ProjectedCoordinateSystem);

        writer.WritePropertyName("originEastingNorthing");
        writer.WriteStartObject();
        writer.WriteNumber("x", transform.OriginEastingNorthing.X);
        writer.WriteNumber("y", transform.OriginEastingNorthing.Y);
        writer.WriteEndObject();

        context.EnsureStringWithinBudget(writer, transform.VerticalDatum);
        writer.WriteNumber("metersPerGameUnit", transform.MetersPerGameUnit);
        writer.WriteString("verticalDatum", transform.VerticalDatum);
        writer.WriteNumber("verticalOriginMeters", transform.VerticalOriginMeters);
        writer.WriteNumber("verticalExaggeration", transform.VerticalExaggeration);
        writer.WriteEndObject();

        writer.WritePropertyName("playableArea");
        WritePolygon(writer, grid.PlayableArea, context);

        if (grid.ImageryPixelsPerSample is { } pixelsPerSample)
        {
            writer.WriteNumber("imageryPixelsPerSample", pixelsPerSample);
        }

        writer.WritePropertyName("slopeThresholds");
        writer.WriteStartObject();
        writer.WriteNumber("impassableDegrees", grid.SlopeThresholds.ImpassableDegrees);
        writer.WriteNumber("unbuildableDegrees", grid.SlopeThresholds.UnbuildableDegrees);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteCrs(Utf8JsonWriter writer, CoordinateSystemId crs)
    {
        writer.WriteStartObject();

        if (crs.EpsgCode is { } epsgCode)
        {
            writer.WriteNumber("epsg", epsgCode);
        }
        else
        {
            writer.WriteNumber("centralMeridianDegrees", crs.CentralMeridianDegrees);
            writer.WriteNumber("scale", crs.Scale);
            writer.WriteNumber("falseEasting", crs.FalseEasting);
            writer.WriteNumber("falseNorthing", crs.FalseNorthing);
        }

        writer.WriteEndObject();
    }

    private static void WritePolygon(Utf8JsonWriter writer, GeoPolygon polygon, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        WritePolygonProperties(writer, polygon, context);
        writer.WriteEndObject();
    }

    private static void WritePolygonProperties(Utf8JsonWriter writer, GeoPolygon polygon, PayloadWriteContext context)
    {
        context.Checkpoint(writer);

        writer.WritePropertyName("outerRing");
        WriteRing(writer, polygon.OuterRing, context);

        writer.WritePropertyName("innerRings");
        writer.WriteStartArray();

        foreach (var ring in polygon.InnerRings)
        {
            context.Checkpoint(writer);
            WriteRing(writer, ring, context);
        }

        writer.WriteEndArray();
    }

    private static void WriteRing(Utf8JsonWriter writer, GeoRing ring, PayloadWriteContext context)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("positions");
        writer.WriteStartArray();

        WritePositions(writer, ring.Positions, context);

        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WritePosition(Utf8JsonWriter writer, GeoPosition position)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", position.Coordinate.X);
        writer.WriteNumber("y", position.Coordinate.Y);

        if (position.AltitudeMeters is { } altitude)
        {
            writer.WriteNumber("altitudeMeters", altitude);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes fixed-size position batches with incremental flushing and cancellation.
    /// </summary>
    private static void WritePositions(
        Utf8JsonWriter writer,
        IReadOnlyList<GeoPosition> positions,
        PayloadWriteContext context)
    {
        var written = 0;

        foreach (var position in positions)
        {
            if (written % PositionsPerFlush == 0)
            {
                context.Checkpoint(writer);
            }

            WritePosition(writer, position);
            written++;
        }
    }

    private static void WriteGeometry(Utf8JsonWriter writer, GeoGeometry geometry, PayloadWriteContext context)
    {
        context.Checkpoint(writer);

        writer.WriteStartObject();

        switch (geometry)
        {
            case GeoPointGeometry point:
                writer.WriteString("type", "point");
                writer.WritePropertyName("position");
                WritePosition(writer, point.Position);
                break;
            case GeoLineString line:
                writer.WriteString("type", "line-string");
                writer.WritePropertyName("positions");
                writer.WriteStartArray();

                WritePositions(writer, line.Positions, context);

                writer.WriteEndArray();
                break;
            case GeoPolygon polygon:
                writer.WriteString("type", "polygon");
                WritePolygonProperties(writer, polygon, context);
                break;
            case GeoMultiGeometry multi:
                writer.WriteString("type", "multi");
                writer.WritePropertyName("geometries");
                writer.WriteStartArray();

                foreach (var nested in multi.Geometries)
                {
                    WriteGeometry(writer, nested, context);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new NotSupportedException($"Unsupported geometry type {geometry.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static void WritePalette(Utf8JsonWriter writer, MaterialPalette palette, PayloadWriteContext context)
    {
        writer.WritePropertyName("palette");
        writer.WriteStartArray();

        foreach (var name in palette.Names)
        {
            context.Checkpoint(writer);
            context.EnsureStringWithinBudget(writer, name);
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
    }

    private static void WriteFeatures(Utf8JsonWriter writer, IReadOnlyList<GeneratedFeature> features, PayloadWriteContext context)
    {
        var registry = new AttributeTableRegistry();

        writer.WritePropertyName("features");
        writer.WriteStartObject();

        writer.WritePropertyName("attributeTables");
        writer.WriteStartArray();

        foreach (var feature in features)
        {
            registry.Register(feature.Attributes);
        }

        foreach (var table in registry.Tables)
        {
            context.Checkpoint(writer);

            writer.WriteStartObject();

            writer.WritePropertyName("entries");
            writer.WriteStartObject();

            foreach (var key in SortedKeys(table))
            {
                // The property name is written between the two budget checks so the value
                // check sees the key's committed bytes; the bounded stream accounts for
                // the colon and other punctuation.
                context.EnsureStringWithinBudget(writer, key);
                writer.WritePropertyName(key);
                context.EnsureStringWithinBudget(writer, table[key]);
                writer.WriteStringValue(table[key]);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("items");
        writer.WriteStartArray();

        foreach (var feature in features)
        {
            context.Checkpoint(writer);
            WriteFeature(writer, feature, registry, context);
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteFeature(Utf8JsonWriter writer, GeneratedFeature feature, AttributeTableRegistry registry, PayloadWriteContext context)
    {
        writer.WriteStartObject();

        writer.WriteString("kind", PayloadEnumTokens<GeneratedFeatureKind>.ToToken(feature.Kind));

        writer.WritePropertyName("geometry");
        WriteGeometry(writer, feature.Geometry, context);

        context.EnsureStringWithinBudget(writer, feature.SourceId);
        writer.WriteString("sourceId", feature.SourceId);
        context.EnsureStringWithinBudget(writer, feature.FeatureId);
        writer.WriteString("featureId", feature.FeatureId);
        context.EnsureStringWithinBudget(writer, feature.PartId);
        writer.WriteString("partId", feature.PartId);
        writer.WriteNumber("ruleIndex", feature.RuleIndex);

        if (registry.IndexOf(feature.Attributes) is { } tableIndex)
        {
            writer.WriteNumber("attributeTable", tableIndex);
        }

        if (feature.Position is { } position)
        {
            writer.WritePropertyName("position");
            writer.WriteStartObject();
            writer.WriteNumber("x", position.X);
            writer.WriteNumber("y", position.Y);
            writer.WriteNumber("z", position.Z);
            writer.WriteEndObject();
        }

        if (feature.ObjectPlacement is { } placement)
        {
            writer.WritePropertyName("objectPlacement");
            writer.WriteStartObject();
            context.EnsureStringWithinBudget(writer, placement.ObjectDefinition);
            writer.WriteString("objectDefinition", placement.ObjectDefinition);
            writer.WriteNumber("orientationDegrees", placement.OrientationDegrees);
            writer.WriteEndObject();
        }

        if (feature.Passable is { } passable)
        {
            writer.WriteBoolean("passable", passable);
        }

        if (feature.Buildable is { } buildable)
        {
            writer.WriteBoolean("buildable", buildable);
        }

        writer.WriteEndObject();
    }

    private static List<string> SortedKeys(IReadOnlyDictionary<string, string> table)
    {
        var keys = new List<string>(table.Keys);
        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    private static void WriteProvenance(Utf8JsonWriter writer, IReadOnlyList<PayloadSourceProvenance> provenance, PayloadWriteContext context)
    {
        writer.WritePropertyName("provenance");
        writer.WriteStartArray();

        foreach (var source in provenance)
        {
            context.Checkpoint(writer);

            writer.WriteStartObject();

            context.EnsureStringWithinBudget(writer, source.SourceId);
            writer.WriteString("sourceId", source.SourceId);
            writer.WriteString("kind", PayloadEnumTokens<ManifestSourceKind>.ToToken(source.Kind));
            writer.WriteString("role", PayloadEnumTokens<ManifestSourceRole>.ToToken(source.Role));
            context.EnsureStringWithinBudget(writer, source.RelativePath);
            writer.WriteString("relativePath", source.RelativePath);
            context.EnsureStringWithinBudget(writer, source.Sha256Hex);
            writer.WriteString("sha256", source.Sha256Hex);
            writer.WriteNumber("lengthBytes", source.LengthBytes);

            writer.WritePropertyName("actualCoordinateSystem");
            WriteCrs(writer, source.ActualCoordinateSystem);

            context.EnsureStringWithinBudget(writer, source.DeclaredVerticalDatum);
            writer.WriteString("declaredVerticalDatum", source.DeclaredVerticalDatum);

            if (source.EmbeddedVerticalDatum is { } embedded)
            {
                context.EnsureStringWithinBudget(writer, embedded);
                writer.WriteString("embeddedVerticalDatum", embedded);
            }

            if (source.Label is { } label)
            {
                context.EnsureStringWithinBudget(writer, label);
                writer.WriteString("label", label);
            }

            if (source.Attribution is { } attribution)
            {
                context.EnsureStringWithinBudget(writer, attribution);
                writer.WriteString("attribution", attribution);
            }

            if (source.HorizontalAccuracyMeters is { } horizontal)
            {
                writer.WriteNumber("horizontalAccuracyMeters", horizontal);
            }

            if (source.VerticalAccuracyMeters is { } vertical)
            {
                writer.WriteNumber("verticalAccuracyMeters", vertical);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteChunks(Utf8JsonWriter writer, IReadOnlyList<PayloadChunkDescriptor> chunks, PayloadWriteContext context)
    {
        writer.WritePropertyName("chunks");
        writer.WriteStartArray();

        foreach (var chunk in chunks)
        {
            context.Checkpoint(writer);

            writer.WriteStartObject();

            writer.WriteNumber("cx", chunk.ChunkX);
            writer.WriteNumber("cy", chunk.ChunkY);

            writer.WritePropertyName("terrain");
            WriteFileReference(writer, chunk.Terrain, context);

            writer.WritePropertyName("materials");
            WriteFileReference(writer, chunk.Materials, context);

            writer.WritePropertyName("masks");
            WriteFileReference(writer, chunk.Masks, context);

            if (chunk.Imagery is { } imagery)
            {
                writer.WritePropertyName("imagery");
                WriteFileReference(writer, imagery, context);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteFileReference(Utf8JsonWriter writer, PayloadFileReference reference, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        context.EnsureStringWithinBudget(writer, reference.Path);
        writer.WriteString("path", reference.Path);
        writer.WriteNumber("lengthBytes", reference.LengthBytes);
        context.EnsureStringWithinBudget(writer, reference.Sha256Hex);
        writer.WriteString("sha256", reference.Sha256Hex);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Assigns one table index per distinct non-empty attribute dictionary, identified by
    /// reference so a dictionary shared across generated features serializes once.
    /// </summary>
    private sealed class AttributeTableRegistry
    {
        private readonly Dictionary<IReadOnlyDictionary<string, string>, int> _indices =
            new(ReferenceEqualityComparer.Instance);

        private readonly List<IReadOnlyDictionary<string, string>> _tables = [];

        public IReadOnlyList<IReadOnlyDictionary<string, string>> Tables => _tables;

        public void Register(IReadOnlyDictionary<string, string> attributes)
        {
            if (attributes.Count == 0 || _indices.ContainsKey(attributes))
            {
                return;
            }

            _indices.Add(attributes, _tables.Count);
            _tables.Add(attributes);
        }

        public int? IndexOf(IReadOnlyDictionary<string, string> attributes)
        {
            return attributes.Count == 0 ? null : _indices[attributes];
        }
    }
}
