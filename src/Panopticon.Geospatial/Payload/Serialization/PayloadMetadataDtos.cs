using System.Collections.Generic;

namespace Panopticon.Geospatial.Payload.Serialization;

// Parsing targets for payload.json. Every member is optional here so the strict parser
// can reject unknown and duplicate members before any list is materialized; conversion
// requires the mandatory members and delegate value checks to the public model
// constructors.

internal sealed record PayloadLayoutDto(
    int? SampleSpacingGameUnits = null,
    int? IntervalsPerChunk = null,
    int? PointsPerChunkAxis = null,
    int? ApronSamplesPerSide = null,
    int? StoredPointsPerChunkAxis = null);

internal sealed record PayloadCrsDto(
    int? EpsgCode = null,
    double? CentralMeridianDegrees = null,
    double? Scale = null,
    double? FalseEasting = null,
    double? FalseNorthing = null);

internal sealed record PayloadTransformDto(
    List<PayloadCrsDto>? SourceCoordinateSystems = null,
    PayloadCrsDto? ProjectedCoordinateSystem = null,
    double? OriginEasting = null,
    double? OriginNorthing = null,
    double? MetersPerGameUnit = null,
    string? VerticalDatum = null,
    double? VerticalOriginMeters = null,
    double? VerticalExaggeration = null);

internal sealed record PayloadPositionDto(double X, double Y, double? AltitudeMeters);

internal sealed record PayloadRingDto(List<PayloadPositionDto>? Positions = null);

internal sealed record PayloadPolygonDto(PayloadRingDto? OuterRing = null, List<PayloadRingDto>? InnerRings = null);

/// <summary>Geometry parsing target; only the members valid for <see cref="Type"/> are set.</summary>
internal sealed record PayloadGeometryDto
{
    public string? Type { get; init; }

    public PayloadPositionDto? Position { get; init; }

    public List<PayloadPositionDto>? Positions { get; init; }

    public PayloadRingDto? OuterRing { get; init; }

    public List<PayloadRingDto>? InnerRings { get; init; }

    public List<PayloadGeometryDto>? Geometries { get; init; }
}

internal sealed record PayloadAttributeTableDto(Dictionary<string, string> Entries);

internal sealed record PayloadFeatureDto(
    string? Kind = null,
    PayloadGeometryDto? Geometry = null,
    string? SourceId = null,
    string? FeatureId = null,
    string? PartId = null,
    int? RuleIndex = null,
    int? AttributeTable = null,
    double? PositionX = null,
    double? PositionY = null,
    double? PositionZ = null,
    string? ObjectDefinition = null,
    double? OrientationDegrees = null,
    bool? Passable = null,
    bool? Buildable = null);

internal sealed record PayloadFeaturesDto(
    List<PayloadAttributeTableDto>? AttributeTables = null,
    List<PayloadFeatureDto>? Items = null);

internal sealed record PayloadGridDto(
    int? ChunksX = null,
    int? ChunksY = null,
    PayloadLayoutDto? Layout = null,
    PayloadTransformDto? Transform = null,
    PayloadPolygonDto? PlayableArea = null,
    int? ImageryPixelsPerSample = null,
    double? ImpassableSlopeDegrees = null,
    double? UnbuildableSlopeDegrees = null);

internal sealed record PayloadFileReferenceDto(
    string? Path = null,
    long? LengthBytes = null,
    string? Sha256 = null);

internal sealed record PayloadChunkDto(
    int? ChunkX = null,
    int? ChunkY = null,
    PayloadFileReferenceDto? Terrain = null,
    PayloadFileReferenceDto? Materials = null,
    PayloadFileReferenceDto? Masks = null,
    PayloadFileReferenceDto? Imagery = null);

internal sealed record PayloadSourceProvenanceDto(
    string? SourceId = null,
    string? Role = null,
    string? Kind = null,
    string? RelativePath = null,
    string? Sha256 = null,
    long? LengthBytes = null,
    PayloadCrsDto? ActualCoordinateSystem = null,
    string? DeclaredVerticalDatum = null,
    string? EmbeddedVerticalDatum = null,
    string? Label = null,
    string? Attribution = null,
    double? HorizontalAccuracyMeters = null,
    double? VerticalAccuracyMeters = null);

internal sealed record PayloadMetadataDto(
    string? Kind = null,
    int? SchemaVersion = null,
    PayloadGridDto? Grid = null,
    List<string>? Palette = null,
    PayloadFeaturesDto? Features = null,
    string? ManifestSha256 = null,
    List<PayloadSourceProvenanceDto>? Provenance = null,
    List<PayloadChunkDto>? Chunks = null);
