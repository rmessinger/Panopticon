using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Mutable parse state shared by one document parse: the active limits, cancellation,
/// and the running coordinate budget charged for every geometry position.
/// </summary>
internal sealed class PayloadParseContext
{
    public PayloadParseContext(GeospatialImportLimits limits, CancellationToken cancellationToken)
    {
        Limits = limits;
        CancellationToken = cancellationToken;
    }

    public GeospatialImportLimits Limits { get; }

    public CancellationToken CancellationToken { get; }

    public long CoordinateCount { get; private set; }

    public void ThrowIfCancellationRequested()
    {
        CancellationToken.ThrowIfCancellationRequested();
    }

    public void ChargeCoordinates(int count)
    {
        CoordinateCount += count;

        if (CoordinateCount > Limits.MaxCoordinateCount)
        {
            throw new PayloadJsonLimitException(
                $"The payload declares more than the maxCoordinateCount limit {Limits.MaxCoordinateCount} geometry positions.");
        }
    }
}

/// <summary>
/// Strict streaming parser for payload JSON: camelCase members, named enums, finite
/// numbers, no unknown or duplicate members, and structural limits enforced before the
/// offending element is added to a DTO list. The reader never materializes more than
/// one DTO per element beyond the declared limits.
/// </summary>
internal static class PayloadJsonReader
{
    private const int MaxGeometryDepth = 32;

    internal static PayloadMetadataDto ParseMetadata(Stream stream, PayloadParseContext context)
    {
        var reader = Begin(stream, context);

        string? kind = null;
        int? schemaVersion = null;
        PayloadGridDto? grid = null;
        List<string>? palette = null;
        PayloadFeaturesDto? features = null;
        string? manifestSha256 = null;
        List<PayloadSourceProvenanceDto>? provenance = null;
        List<PayloadChunkDto>? chunks = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "kind":
                    kind = StringValue(ref reader);
                    break;
                case "schemaVersion":
                    schemaVersion = Int32Value(ref reader);
                    break;
                case "grid":
                    grid = ParseGrid(ref reader, context);
                    break;
                case "palette":
                    palette = ParsePalette(ref reader, context);
                    break;
                case "features":
                    features = ParseFeatures(ref reader, context);
                    break;
                case "manifestSha256":
                    manifestSha256 = StringValue(ref reader);
                    break;
                case "provenance":
                    provenance = ParseProvenance(ref reader, context);
                    break;
                case "chunks":
                    chunks = ParseChunks(ref reader, context);
                    break;
                default:
                    throw UnknownMember(name, "the payload root");
            }
        }

        End(ref reader);

        return new PayloadMetadataDto(kind, schemaVersion, grid, palette, features, manifestSha256, provenance, chunks);
    }

    internal static PayloadReportDto ParseReport(Stream stream, PayloadParseContext context)
    {
        var reader = Begin(stream, context);

        string? kind = null;
        int? schemaVersion = null;
        List<PayloadDiagnosticDto>? diagnostics = null;
        PayloadTerrainDto? terrain = null;
        PayloadImageryDto? imagery = null;
        PayloadSamplingDto? classificationSampling = null;
        PayloadMaskDto? masks = null;
        PayloadFeaturesSummaryDto? featuresSummary = null;
        List<PayloadValidationCheckDto>? validationChecks = null;
        string? payloadSha256 = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "kind":
                    kind = StringValue(ref reader);
                    break;
                case "schemaVersion":
                    schemaVersion = Int32Value(ref reader);
                    break;
                case "diagnostics":
                    diagnostics = ParseDiagnostics(ref reader, context);
                    break;
                case "terrain":
                    terrain = ParseTerrain(ref reader, context);
                    break;
                case "imagery":
                    imagery = ParseImagery(ref reader, context);
                    break;
                case "classificationSampling":
                    classificationSampling = ParseSampling(ref reader, context);
                    break;
                case "masks":
                    masks = ParseMasks(ref reader);
                    break;
                case "features":
                    featuresSummary = ParseFeaturesSummary(ref reader, context);
                    break;
                case "validationChecks":
                    validationChecks = ParseValidationChecks(ref reader, context);
                    break;
                case "payloadSha256":
                    payloadSha256 = StringValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "the report root");
            }
        }

        End(ref reader);

        return new PayloadReportDto(
            kind,
            schemaVersion,
            diagnostics,
            terrain,
            imagery,
            classificationSampling,
            masks,
            featuresSummary,
            validationChecks,
            payloadSha256);
    }

    private static PayloadGridDto ParseGrid(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'grid'");

        int? chunksX = null;
        int? chunksY = null;
        PayloadLayoutDto? layout = null;
        PayloadTransformDto? transform = null;
        PayloadPolygonDto? playableArea = null;
        int? imageryPixelsPerSample = null;
        double? impassableSlopeDegrees = null;
        double? unbuildableSlopeDegrees = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "chunksX":
                    chunksX = Int32Value(ref reader);
                    break;
                case "chunksY":
                    chunksY = Int32Value(ref reader);
                    break;
                case "layout":
                    layout = ParseLayout(ref reader);
                    break;
                case "transform":
                    transform = ParseTransform(ref reader);
                    break;
                case "playableArea":
                    playableArea = ParsePolygon(ref reader, context);
                    break;
                case "imageryPixelsPerSample":
                    imageryPixelsPerSample = Int32Value(ref reader);
                    break;
                case "slopeThresholds":
                    (impassableSlopeDegrees, unbuildableSlopeDegrees) = ParseSlopeThresholds(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'grid'");
            }
        }

        if (chunksX is { } gridX && gridX > PayloadGridDefinition.MaxChunksPerAxis)
        {
            throw new PayloadJsonLimitException(
                $"grid.chunksX {gridX} exceeds the supported maximum {PayloadGridDefinition.MaxChunksPerAxis}.");
        }

        if (chunksY is { } gridY && gridY > PayloadGridDefinition.MaxChunksPerAxis)
        {
            throw new PayloadJsonLimitException(
                $"grid.chunksY {gridY} exceeds the supported maximum {PayloadGridDefinition.MaxChunksPerAxis}.");
        }

        if (chunksX is { } width && chunksY is { } height && (long)width * height > context.Limits.MaxChunkCount)
        {
            throw new PayloadJsonLimitException(
                $"grid declares {width} x {height} chunks, exceeding the maxChunkCount limit {context.Limits.MaxChunkCount}.");
        }

        return new PayloadGridDto(
            chunksX,
            chunksY,
            layout,
            transform,
            playableArea,
            imageryPixelsPerSample,
            impassableSlopeDegrees,
            unbuildableSlopeDegrees);
    }

    private static PayloadLayoutDto ParseLayout(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'layout'");

        int? sampleSpacing = null;
        int? intervals = null;
        int? points = null;
        int? apron = null;
        int? stored = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sampleSpacingGameUnits":
                    sampleSpacing = Int32Value(ref reader);
                    break;
                case "intervalsPerChunk":
                    intervals = Int32Value(ref reader);
                    break;
                case "pointsPerChunkAxis":
                    points = Int32Value(ref reader);
                    break;
                case "apronSamplesPerSide":
                    apron = Int32Value(ref reader);
                    break;
                case "storedPointsPerChunkAxis":
                    stored = Int32Value(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'layout'");
            }
        }

        return new PayloadLayoutDto(sampleSpacing, intervals, points, apron, stored);
    }

    private static PayloadTransformDto ParseTransform(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'transform'");

        List<PayloadCrsDto>? sources = null;
        PayloadCrsDto? projected = null;
        double? originEasting = null;
        double? originNorthing = null;
        double? metersPerGameUnit = null;
        string? verticalDatum = null;
        double? verticalOriginMeters = null;
        double? verticalExaggeration = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sourceCoordinateSystems":
                    sources = ParseCrsArray(ref reader);
                    break;
                case "projectedCoordinateSystem":
                    projected = ParseCrs(ref reader);
                    break;
                case "originEastingNorthing":
                    (originEasting, originNorthing) = ParseOrigin(ref reader);
                    break;
                case "metersPerGameUnit":
                    metersPerGameUnit = FiniteDoubleValue(ref reader);
                    break;
                case "verticalDatum":
                    verticalDatum = StringValue(ref reader);
                    break;
                case "verticalOriginMeters":
                    verticalOriginMeters = FiniteDoubleValue(ref reader);
                    break;
                case "verticalExaggeration":
                    verticalExaggeration = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'transform'");
            }
        }

        return new PayloadTransformDto(
            sources,
            projected,
            originEasting,
            originNorthing,
            metersPerGameUnit,
            verticalDatum,
            verticalOriginMeters,
            verticalExaggeration);
    }

    private static (double? Easting, double? Northing) ParseOrigin(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'originEastingNorthing'");

        double? x = null;
        double? y = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "x":
                    x = FiniteDoubleValue(ref reader);
                    break;
                case "y":
                    y = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'originEastingNorthing'");
            }
        }

        return (x, y);
    }

    private static List<PayloadCrsDto> ParseCrsArray(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'sourceCoordinateSystems'");

        var values = new List<PayloadCrsDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(ParseCrs(ref reader));
            ReadNext(ref reader);
        }

        return values;
    }

    private static PayloadCrsDto ParseCrs(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a coordinate system");

        int? epsg = null;
        double? centralMeridian = null;
        double? scale = null;
        double? falseEasting = null;
        double? falseNorthing = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "epsg":
                    epsg = Int32Value(ref reader);
                    break;
                case "centralMeridianDegrees":
                    centralMeridian = FiniteDoubleValue(ref reader);
                    break;
                case "scale":
                    scale = FiniteDoubleValue(ref reader);
                    break;
                case "falseEasting":
                    falseEasting = FiniteDoubleValue(ref reader);
                    break;
                case "falseNorthing":
                    falseNorthing = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a coordinate system");
            }
        }

        return new PayloadCrsDto(epsg, centralMeridian, scale, falseEasting, falseNorthing);
    }

    private static (double? Impassable, double? Unbuildable) ParseSlopeThresholds(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'slopeThresholds'");

        double? impassable = null;
        double? unbuildable = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "impassableDegrees":
                    impassable = FiniteDoubleValue(ref reader);
                    break;
                case "unbuildableDegrees":
                    unbuildable = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'slopeThresholds'");
            }
        }

        return (impassable, unbuildable);
    }

    private static List<string> ParsePalette(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'palette'");

        var names = new List<string>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (names.Count >= MaterialPalette.MaxEntries)
            {
                throw new PayloadJsonLimitException(
                    $"palette holds more than the supported maximum of {MaterialPalette.MaxEntries} entries.");
            }

            names.Add(StringValue(ref reader));
            ReadNext(ref reader);
        }

        return names;
    }

    private static PayloadPolygonDto ParsePolygon(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a polygon");

        PayloadRingDto? outerRing = null;
        List<PayloadRingDto>? innerRings = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "outerRing":
                    outerRing = ParseRing(ref reader, context);
                    break;
                case "innerRings":
                    innerRings = ParseRingArray(ref reader, context);
                    break;
                default:
                    throw UnknownMember(name, "a polygon");
            }
        }

        return new PayloadPolygonDto(outerRing, innerRings);
    }

    private static PayloadRingDto ParseRing(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a ring");

        List<PayloadPositionDto>? positions = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "positions":
                    positions = ParsePositionArray(ref reader, context);
                    break;
                default:
                    throw UnknownMember(name, "a ring");
            }
        }

        return new PayloadRingDto(positions);
    }

    private static List<PayloadRingDto> ParseRingArray(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'innerRings'");

        var rings = new List<PayloadRingDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            rings.Add(ParseRing(ref reader, context));
            ReadNext(ref reader);
        }

        return rings;
    }

    private static PayloadPositionDto ParsePosition(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a position");

        double? x = null;
        double? y = null;
        double? altitude = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "x":
                    x = FiniteDoubleValue(ref reader);
                    break;
                case "y":
                    y = FiniteDoubleValue(ref reader);
                    break;
                case "altitudeMeters":
                    altitude = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a position");
            }
        }

        return new PayloadPositionDto(
            Require(x, "a position", "x"),
            Require(y, "a position", "y"),
            altitude);
    }

    private static List<PayloadPositionDto> ParsePositionArray(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'positions'");

        var positions = new List<PayloadPositionDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            context.ChargeCoordinates(1);
            positions.Add(ParsePosition(ref reader));
            ReadNext(ref reader);
        }

        return positions;
    }

    private static PayloadFeaturesDto ParseFeatures(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'features'");

        List<PayloadAttributeTableDto>? tables = null;
        List<PayloadFeatureDto>? items = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "attributeTables":
                    tables = ParseAttributeTables(ref reader, context);
                    break;
                case "items":
                    items = ParseFeatureItems(ref reader, context);
                    break;
                default:
                    throw UnknownMember(name, "'features'");
            }
        }

        return new PayloadFeaturesDto(tables, items);
    }

    private static List<PayloadAttributeTableDto> ParseAttributeTables(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'attributeTables'");

        var tables = new List<PayloadAttributeTableDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (tables.Count >= context.Limits.MaxFeatureCount)
            {
                throw new PayloadJsonLimitException(
                    $"attributeTables holds more than the maxFeatureCount limit {context.Limits.MaxFeatureCount}.");
            }

            tables.Add(ParseAttributeTable(ref reader));
            ReadNext(ref reader);
        }

        return tables;
    }

    private static PayloadAttributeTableDto ParseAttributeTable(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "an attribute table");

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "entries":
                    ParseEntries(ref reader, entries);
                    break;
                default:
                    throw UnknownMember(name, "an attribute table");
            }
        }

        return new PayloadAttributeTableDto(entries);
    }

    private static void ParseEntries(ref PayloadJsonTokenReader reader, Dictionary<string, string> entries)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'entries'");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            entries.Add(name, StringValue(ref reader));
        }
    }

    private static List<PayloadFeatureDto> ParseFeatureItems(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "feature 'items'");

        var items = new List<PayloadFeatureDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (items.Count >= context.Limits.MaxFeatureCount)
            {
                throw new PayloadJsonLimitException(
                    $"features.items holds more than the maxFeatureCount limit {context.Limits.MaxFeatureCount}.");
            }

            items.Add(ParseFeature(ref reader, context));
            ReadNext(ref reader);
        }

        return items;
    }

    private static PayloadFeatureDto ParseFeature(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a feature");

        string? kind = null;
        PayloadGeometryDto? geometry = null;
        string? sourceId = null;
        string? featureId = null;
        string? partId = null;
        int? ruleIndex = null;
        int? attributeTable = null;
        double? positionX = null;
        double? positionY = null;
        double? positionZ = null;
        string? objectDefinition = null;
        double? orientationDegrees = null;
        bool? passable = null;
        bool? buildable = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "kind":
                    kind = StringValue(ref reader);
                    break;
                case "geometry":
                    geometry = ParseGeometry(ref reader, context, depth: 0);
                    break;
                case "sourceId":
                    sourceId = StringValue(ref reader);
                    break;
                case "featureId":
                    featureId = StringValue(ref reader);
                    break;
                case "partId":
                    partId = StringValue(ref reader);
                    break;
                case "ruleIndex":
                    ruleIndex = Int32Value(ref reader);
                    break;
                case "attributeTable":
                    attributeTable = Int32Value(ref reader);
                    break;
                case "position":
                    (positionX, positionY, positionZ) = ParseGamePosition(ref reader);
                    break;
                case "objectPlacement":
                    (objectDefinition, orientationDegrees) = ParseObjectPlacement(ref reader);
                    break;
                case "passable":
                    passable = BoolValue(ref reader);
                    break;
                case "buildable":
                    buildable = BoolValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a feature");
            }
        }

        return new PayloadFeatureDto(
            kind,
            geometry,
            sourceId,
            featureId,
            partId,
            ruleIndex,
            attributeTable,
            positionX,
            positionY,
            positionZ,
            objectDefinition,
            orientationDegrees,
            passable,
            buildable);
    }

    private static PayloadGeometryDto ParseGeometry(ref PayloadJsonTokenReader reader, PayloadParseContext context, int depth)
    {
        if (depth > MaxGeometryDepth)
        {
            throw new PayloadJsonLimitException($"Geometry nests deeper than the supported maximum of {MaxGeometryDepth}.");
        }

        Expect(ref reader, JsonTokenType.StartObject, "a geometry");

        string? type = null;
        PayloadPositionDto? position = null;
        List<PayloadPositionDto>? positions = null;
        PayloadRingDto? outerRing = null;
        List<PayloadRingDto>? innerRings = null;
        List<PayloadGeometryDto>? geometries = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "type":
                    type = StringValue(ref reader);
                    break;
                case "position":
                    context.ChargeCoordinates(1);
                    position = ParsePosition(ref reader);
                    break;
                case "positions":
                    positions = ParsePositionArray(ref reader, context);
                    break;
                case "outerRing":
                    outerRing = ParseRing(ref reader, context);
                    break;
                case "innerRings":
                    innerRings = ParseRingArray(ref reader, context);
                    break;
                case "geometries":
                    geometries = ParseGeometryArray(ref reader, context, depth + 1);
                    break;
                default:
                    throw UnknownMember(name, "a geometry");
            }
        }

        return new PayloadGeometryDto
        {
            Type = type,
            Position = position,
            Positions = positions,
            OuterRing = outerRing,
            InnerRings = innerRings,
            Geometries = geometries,
        };
    }

    private static List<PayloadGeometryDto> ParseGeometryArray(ref PayloadJsonTokenReader reader, PayloadParseContext context, int depth)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'geometries'");

        var geometries = new List<PayloadGeometryDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            geometries.Add(ParseGeometry(ref reader, context, depth));
            ReadNext(ref reader);
        }

        return geometries;
    }

    private static (double? X, double? Y, double? Z) ParseGamePosition(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'position'");

        double? x = null;
        double? y = null;
        double? z = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "x":
                    x = FiniteDoubleValue(ref reader);
                    break;
                case "y":
                    y = FiniteDoubleValue(ref reader);
                    break;
                case "z":
                    z = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'position'");
            }
        }

        return (x, y, z);
    }

    private static (string? ObjectDefinition, double? OrientationDegrees) ParseObjectPlacement(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'objectPlacement'");

        string? objectDefinition = null;
        double? orientationDegrees = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "objectDefinition":
                    objectDefinition = StringValue(ref reader);
                    break;
                case "orientationDegrees":
                    orientationDegrees = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'objectPlacement'");
            }
        }

        return (objectDefinition, orientationDegrees);
    }

    private static List<PayloadSourceProvenanceDto> ParseProvenance(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'provenance'");

        var sources = new List<PayloadSourceProvenanceDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            sources.Add(ParseProvenanceSource(ref reader));
            ReadNext(ref reader);
        }

        return sources;
    }

    private static PayloadSourceProvenanceDto ParseProvenanceSource(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a provenance entry");

        string? sourceId = null;
        string? role = null;
        string? kind = null;
        string? relativePath = null;
        string? sha256 = null;
        long? lengthBytes = null;
        PayloadCrsDto? actualCoordinateSystem = null;
        string? declaredVerticalDatum = null;
        string? embeddedVerticalDatum = null;
        string? label = null;
        string? attribution = null;
        double? horizontalAccuracyMeters = null;
        double? verticalAccuracyMeters = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sourceId":
                    sourceId = StringValue(ref reader);
                    break;
                case "role":
                    role = StringValue(ref reader);
                    break;
                case "kind":
                    kind = StringValue(ref reader);
                    break;
                case "relativePath":
                    relativePath = StringValue(ref reader);
                    break;
                case "sha256":
                    sha256 = StringValue(ref reader);
                    break;
                case "lengthBytes":
                    lengthBytes = Int64Value(ref reader);
                    break;
                case "actualCoordinateSystem":
                    actualCoordinateSystem = ParseCrs(ref reader);
                    break;
                case "declaredVerticalDatum":
                    declaredVerticalDatum = StringValue(ref reader);
                    break;
                case "embeddedVerticalDatum":
                    embeddedVerticalDatum = StringValue(ref reader);
                    break;
                case "label":
                    label = StringValue(ref reader);
                    break;
                case "attribution":
                    attribution = StringValue(ref reader);
                    break;
                case "horizontalAccuracyMeters":
                    horizontalAccuracyMeters = FiniteDoubleValue(ref reader);
                    break;
                case "verticalAccuracyMeters":
                    verticalAccuracyMeters = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a provenance entry");
            }
        }

        return new PayloadSourceProvenanceDto(
            sourceId,
            role,
            kind,
            relativePath,
            sha256,
            lengthBytes,
            actualCoordinateSystem,
            declaredVerticalDatum,
            embeddedVerticalDatum,
            label,
            attribution,
            horizontalAccuracyMeters,
            verticalAccuracyMeters);
    }

    private static List<PayloadChunkDto> ParseChunks(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'chunks'");

        var chunks = new List<PayloadChunkDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (chunks.Count >= context.Limits.MaxChunkCount)
            {
                throw new PayloadJsonLimitException(
                    $"chunks holds more than the maxChunkCount limit {context.Limits.MaxChunkCount}.");
            }

            chunks.Add(ParseChunk(ref reader));
            ReadNext(ref reader);
        }

        return chunks;
    }

    private static PayloadChunkDto ParseChunk(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a chunk descriptor");

        int? chunkX = null;
        int? chunkY = null;
        PayloadFileReferenceDto? terrain = null;
        PayloadFileReferenceDto? materials = null;
        PayloadFileReferenceDto? masks = null;
        PayloadFileReferenceDto? imagery = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "cx":
                    chunkX = Int32Value(ref reader);
                    break;
                case "cy":
                    chunkY = Int32Value(ref reader);
                    break;
                case "terrain":
                    terrain = ParseFileReference(ref reader);
                    break;
                case "materials":
                    materials = ParseFileReference(ref reader);
                    break;
                case "masks":
                    masks = ParseFileReference(ref reader);
                    break;
                case "imagery":
                    imagery = ParseFileReference(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a chunk descriptor");
            }
        }

        return new PayloadChunkDto(chunkX, chunkY, terrain, materials, masks, imagery);
    }

    private static PayloadFileReferenceDto ParseFileReference(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a file reference");

        string? path = null;
        long? lengthBytes = null;
        string? sha256 = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "path":
                    path = StringValue(ref reader);
                    break;
                case "lengthBytes":
                    lengthBytes = Int64Value(ref reader);
                    break;
                case "sha256":
                    sha256 = StringValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a file reference");
            }
        }

        return new PayloadFileReferenceDto(path, lengthBytes, sha256);
    }

    private static List<PayloadDiagnosticDto> ParseDiagnostics(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'diagnostics'");

        var diagnostics = new List<PayloadDiagnosticDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            diagnostics.Add(ParseDiagnostic(ref reader));
            ReadNext(ref reader);
        }

        return diagnostics;
    }

    private static PayloadDiagnosticDto ParseDiagnostic(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a diagnostic");

        string? code = null;
        string? stage = null;
        string? severity = null;
        string? message = null;
        string? sourcePath = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "code":
                    code = StringValue(ref reader);
                    break;
                case "stage":
                    stage = StringValue(ref reader);
                    break;
                case "severity":
                    severity = StringValue(ref reader);
                    break;
                case "message":
                    message = StringValue(ref reader);
                    break;
                case "sourcePath":
                    sourcePath = StringValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a diagnostic");
            }
        }

        return new PayloadDiagnosticDto(code, stage, severity, message, sourcePath);
    }

    private static PayloadTerrainDto ParseTerrain(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'terrain'");

        PayloadSamplingDto? sampling = null;
        long? chunkCount = null;
        float? minHeight = null;
        float? maxHeight = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sampling":
                    sampling = ParseSampling(ref reader, context);
                    break;
                case "chunkCount":
                    chunkCount = Int64Value(ref reader);
                    break;
                case "minHeightGameUnits":
                    minHeight = FiniteSingleValue(ref reader);
                    break;
                case "maxHeightGameUnits":
                    maxHeight = FiniteSingleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'terrain'");
            }
        }

        return new PayloadTerrainDto(sampling, chunkCount, minHeight, maxHeight);
    }

    private static PayloadImageryDto ParseImagery(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'imagery'");

        PayloadSamplingDto? sampling = null;
        long? chunkCount = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sampling":
                    sampling = ParseSampling(ref reader, context);
                    break;
                case "chunkCount":
                    chunkCount = Int64Value(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'imagery'");
            }
        }

        return new PayloadImageryDto(sampling, chunkCount);
    }

    private static PayloadMaskDto ParseMasks(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'masks'");

        double? impassable = null;
        double? unbuildable = null;
        double? exaggeration = null;
        long? chunkCount = null;
        long? impassableCells = null;
        long? unbuildableCells = null;
        long? slopeImpassableCells = null;
        long? slopeUnbuildableCells = null;
        long? waterRules = null;
        long? passabilityRules = null;
        long? buildabilityRules = null;
        long? playableForced = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "impassableThresholdDegrees":
                    impassable = FiniteDoubleValue(ref reader);
                    break;
                case "unbuildableThresholdDegrees":
                    unbuildable = FiniteDoubleValue(ref reader);
                    break;
                case "verticalExaggeration":
                    exaggeration = FiniteDoubleValue(ref reader);
                    break;
                case "chunkCount":
                    chunkCount = Int64Value(ref reader);
                    break;
                case "impassableCellCount":
                    impassableCells = Int64Value(ref reader);
                    break;
                case "unbuildableCellCount":
                    unbuildableCells = Int64Value(ref reader);
                    break;
                case "slopeImpassableCellCount":
                    slopeImpassableCells = Int64Value(ref reader);
                    break;
                case "slopeUnbuildableCellCount":
                    slopeUnbuildableCells = Int64Value(ref reader);
                    break;
                case "waterRuleApplicationCount":
                    waterRules = Int64Value(ref reader);
                    break;
                case "passabilityRuleApplicationCount":
                    passabilityRules = Int64Value(ref reader);
                    break;
                case "buildabilityRuleApplicationCount":
                    buildabilityRules = Int64Value(ref reader);
                    break;
                case "playableForcedCellCount":
                    playableForced = Int64Value(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'masks'");
            }
        }

        return new PayloadMaskDto(
            impassable,
            unbuildable,
            exaggeration,
            chunkCount,
            impassableCells,
            unbuildableCells,
            slopeImpassableCells,
            slopeUnbuildableCells,
            waterRules,
            passabilityRules,
            buildabilityRules,
            playableForced);
    }

    private static PayloadFeaturesSummaryDto ParseFeaturesSummary(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "report 'features'");

        long? totalCount = null;
        List<PayloadKindCountDto>? countsByKind = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "totalCount":
                    totalCount = Int64Value(ref reader);
                    break;
                case "countsByKind":
                    countsByKind = ParseKindCounts(ref reader, context);
                    break;
                default:
                    throw UnknownMember(name, "report 'features'");
            }
        }

        return new PayloadFeaturesSummaryDto(totalCount, countsByKind);
    }

    private static List<PayloadKindCountDto> ParseKindCounts(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'countsByKind'");

        var maxKinds = Enum.GetValues<GeneratedFeatureKind>().Length;
        var seenKinds = new HashSet<string>(StringComparer.Ordinal);
        var counts = new List<PayloadKindCountDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (counts.Count >= maxKinds)
            {
                throw new PayloadJsonLimitException(
                    $"features.countsByKind holds more than the {maxKinds} defined feature kinds.");
            }

            var entry = ParseKindCount(ref reader);
            if (!seenKinds.Add(entry.Kind ?? string.Empty))
            {
                throw new PayloadJsonSchemaException($"Duplicate feature kind '{entry.Kind}' in countsByKind.");
            }

            counts.Add(entry);
            ReadNext(ref reader);
        }

        return counts;
    }

    private static PayloadKindCountDto ParseKindCount(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a kind count");

        string? kind = null;
        long? count = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "kind":
                    kind = StringValue(ref reader);
                    break;
                case "count":
                    count = Int64Value(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a kind count");
            }
        }

        return new PayloadKindCountDto(kind, count);
    }

    private static PayloadSamplingDto ParseSampling(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'sampling'");

        long? sampleCount = null;
        long? failedSampleCount = null;
        long? noDataFillCount = null;
        long? outsideFillCount = null;
        List<PayloadKernelCountDto>? kernelCounts = null;
        PayloadObservedRangesDto? observedRanges = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sampleCount":
                    sampleCount = Int64Value(ref reader);
                    break;
                case "failedSampleCount":
                    failedSampleCount = Int64Value(ref reader);
                    break;
                case "noDataFillCount":
                    noDataFillCount = Int64Value(ref reader);
                    break;
                case "outsideFillCount":
                    outsideFillCount = Int64Value(ref reader);
                    break;
                case "kernelCounts":
                    kernelCounts = ParseKernelCounts(ref reader, context);
                    break;
                case "observedRanges":
                    observedRanges = ParseObservedRanges(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'sampling'");
            }
        }

        return new PayloadSamplingDto(
            sampleCount,
            failedSampleCount,
            noDataFillCount,
            outsideFillCount,
            kernelCounts,
            observedRanges);
    }

    private static List<PayloadKernelCountDto> ParseKernelCounts(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'kernelCounts'");

        var maxKernels = Enum.GetValues<SampleKernel>().Length;
        var seenKernels = new HashSet<string>(StringComparer.Ordinal);
        var counts = new List<PayloadKernelCountDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            if (counts.Count >= maxKernels)
            {
                throw new PayloadJsonLimitException(
                    $"sampling.kernelCounts holds more than the {maxKernels} defined kernels.");
            }

            var entry = ParseKernelCount(ref reader);
            if (!seenKernels.Add(entry.Kernel ?? string.Empty))
            {
                throw new PayloadJsonSchemaException($"Duplicate kernel '{entry.Kernel}' in kernelCounts.");
            }

            counts.Add(entry);
            ReadNext(ref reader);
        }

        return counts;
    }

    private static PayloadKernelCountDto ParseKernelCount(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a kernel count");

        string? kernel = null;
        long? count = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "kernel":
                    kernel = StringValue(ref reader);
                    break;
                case "count":
                    count = Int64Value(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a kernel count");
            }
        }

        return new PayloadKernelCountDto(kernel, count);
    }

    private static PayloadObservedRangesDto ParseObservedRanges(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "'observedRanges'");

        PayloadRangeDto? sourceGsdX = null;
        PayloadRangeDto? sourceGsdY = null;
        PayloadRangeDto? outputSpacingX = null;
        PayloadRangeDto? outputSpacingY = null;
        PayloadRangeDto? gsdRatioX = null;
        PayloadRangeDto? gsdRatioY = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "sourceGsdXMeters":
                    sourceGsdX = ParseRange(ref reader);
                    break;
                case "sourceGsdYMeters":
                    sourceGsdY = ParseRange(ref reader);
                    break;
                case "outputSpacingXMeters":
                    outputSpacingX = ParseRange(ref reader);
                    break;
                case "outputSpacingYMeters":
                    outputSpacingY = ParseRange(ref reader);
                    break;
                case "gsdRatioX":
                    gsdRatioX = ParseRange(ref reader);
                    break;
                case "gsdRatioY":
                    gsdRatioY = ParseRange(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "'observedRanges'");
            }
        }

        return new PayloadObservedRangesDto(sourceGsdX, sourceGsdY, outputSpacingX, outputSpacingY, gsdRatioX, gsdRatioY);
    }

    private static PayloadRangeDto ParseRange(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "an observed range");

        double? min = null;
        double? max = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "min":
                    min = FiniteDoubleValue(ref reader);
                    break;
                case "max":
                    max = FiniteDoubleValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "an observed range");
            }
        }

        return new PayloadRangeDto(min, max);
    }

    private static List<PayloadValidationCheckDto> ParseValidationChecks(ref PayloadJsonTokenReader reader, PayloadParseContext context)
    {
        Expect(ref reader, JsonTokenType.StartArray, "'validationChecks'");

        var checks = new List<PayloadValidationCheckDto>();
        ReadNext(ref reader);
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            context.ThrowIfCancellationRequested();

            checks.Add(ParseValidationCheck(ref reader));
            ReadNext(ref reader);
        }

        return checks;
    }

    private static PayloadValidationCheckDto ParseValidationCheck(ref PayloadJsonTokenReader reader)
    {
        Expect(ref reader, JsonTokenType.StartObject, "a validation check");

        string? category = null;
        string? checkName = null;
        string? status = null;
        double? expected = null;
        double? actual = null;
        double? tolerance = null;
        string? units = null;
        string? message = null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (NextMember(ref reader, seen, out var name))
        {
            switch (name)
            {
                case "category":
                    category = StringValue(ref reader);
                    break;
                case "name":
                    checkName = StringValue(ref reader);
                    break;
                case "status":
                    status = StringValue(ref reader);
                    break;
                case "expected":
                    expected = FiniteDoubleValue(ref reader);
                    break;
                case "actual":
                    actual = FiniteDoubleValue(ref reader);
                    break;
                case "tolerance":
                    tolerance = FiniteDoubleValue(ref reader);
                    break;
                case "units":
                    units = StringValue(ref reader);
                    break;
                case "message":
                    message = StringValue(ref reader);
                    break;
                default:
                    throw UnknownMember(name, "a validation check");
            }
        }

        return new PayloadValidationCheckDto(category, checkName, status, expected, actual, tolerance, units, message);
    }

    private static PayloadJsonTokenReader Begin(Stream stream, PayloadParseContext context)
    {
        var reader = new PayloadJsonTokenReader(stream, context.Limits.MaxPayloadMetadataBytes, context.CancellationToken);

        if (!reader.Read())
        {
            throw new PayloadJsonSchemaException("The JSON document is empty.");
        }

        Expect(ref reader, JsonTokenType.StartObject, "the document root");

        return reader;
    }

    private static void End(ref PayloadJsonTokenReader reader)
    {
        if (reader.Read())
        {
            throw new PayloadJsonSchemaException("The JSON document has trailing content after the root object.");
        }
    }

    private static bool NextMember(ref PayloadJsonTokenReader reader, HashSet<string> seen, out string name)
    {
        if (!reader.Read())
        {
            throw new PayloadJsonSchemaException("The JSON document is truncated.");
        }

        if (reader.TokenType == JsonTokenType.EndObject)
        {
            name = null!;
            return false;
        }

        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new PayloadJsonSchemaException($"Expected an object member name; found {reader.TokenType}.");
        }

        name = reader.GetString()!;
        if (!seen.Add(name))
        {
            throw new PayloadJsonSchemaException($"Duplicate member '{name}'.");
        }

        ReadNext(ref reader);

        return true;
    }

    private static void ReadNext(ref PayloadJsonTokenReader reader)
    {
        if (!reader.Read())
        {
            throw new PayloadJsonSchemaException("The JSON document is truncated.");
        }
    }

    private static void Expect(ref PayloadJsonTokenReader reader, JsonTokenType tokenType, string what)
    {
        if (reader.TokenType != tokenType)
        {
            throw new PayloadJsonSchemaException($"Expected {what}; found {reader.TokenType}.");
        }
    }

    private static T Require<T>(T? value, string where, string member)
        where T : struct
    {
        if (value is null)
        {
            throw new PayloadJsonSchemaException($"The member '{member}' is required in {where}.");
        }

        return value.Value;
    }

    private static string StringValue(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new PayloadJsonSchemaException($"Expected a string value; found {reader.TokenType}.");
        }

        return reader.GetString()!;
    }

    private static int Int32Value(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
        {
            throw new PayloadJsonSchemaException($"Expected a 32-bit integer; found {reader.TokenType}.");
        }

        return value;
    }

    private static long Int64Value(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var value))
        {
            throw new PayloadJsonSchemaException($"Expected a 64-bit integer; found {reader.TokenType}.");
        }

        return value;
    }

    private static double FiniteDoubleValue(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new PayloadJsonSchemaException($"Expected a number; found {reader.TokenType}.");
        }

        var value = reader.GetDouble();

        if (!double.IsFinite(value))
        {
            throw new PayloadJsonSchemaException("Numbers must be finite.");
        }

        return value;
    }

    private static float FiniteSingleValue(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new PayloadJsonSchemaException($"Expected a number; found {reader.TokenType}.");
        }

        var value = reader.GetSingle();

        if (!float.IsFinite(value))
        {
            throw new PayloadJsonSchemaException("Numbers must be finite.");
        }

        return value;
    }

    private static bool BoolValue(ref PayloadJsonTokenReader reader)
    {
        if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
        {
            throw new PayloadJsonSchemaException($"Expected a boolean; found {reader.TokenType}.");
        }

        return reader.GetBoolean();
    }

    private static PayloadJsonSchemaException UnknownMember(string name, string where)
    {
        return new PayloadJsonSchemaException($"Unknown member '{name}' in {where}.");
    }
}
