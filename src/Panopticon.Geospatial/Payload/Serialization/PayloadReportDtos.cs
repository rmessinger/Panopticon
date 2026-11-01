using System.Collections.Generic;

namespace Panopticon.Geospatial.Payload.Serialization;

// Parsing targets for report.json; see PayloadMetadataDtos for the member conventions.

internal sealed record PayloadDiagnosticDto(
    string? Code = null,
    string? Stage = null,
    string? Severity = null,
    string? Message = null,
    string? SourcePath = null);

internal sealed record PayloadKernelCountDto(string? Kernel = null, long? Count = null);

internal sealed record PayloadRangeDto(double? Min = null, double? Max = null);

internal sealed record PayloadObservedRangesDto(
    PayloadRangeDto? SourceGsdXMeters = null,
    PayloadRangeDto? SourceGsdYMeters = null,
    PayloadRangeDto? OutputSpacingXMeters = null,
    PayloadRangeDto? OutputSpacingYMeters = null,
    PayloadRangeDto? GsdRatioX = null,
    PayloadRangeDto? GsdRatioY = null);

internal sealed record PayloadSamplingDto(
    long? SampleCount = null,
    long? FailedSampleCount = null,
    long? NoDataFillCount = null,
    long? OutsideFillCount = null,
    List<PayloadKernelCountDto>? KernelCounts = null,
    PayloadObservedRangesDto? ObservedRanges = null);

internal sealed record PayloadTerrainDto(
    PayloadSamplingDto? Sampling = null,
    long? ChunkCount = null,
    float? MinHeight = null,
    float? MaxHeight = null);

internal sealed record PayloadImageryDto(
    PayloadSamplingDto? Sampling = null,
    long? ChunkCount = null);

internal sealed record PayloadMaskDto(
    double? ImpassableThresholdDegrees = null,
    double? UnbuildableThresholdDegrees = null,
    double? VerticalExaggeration = null,
    long? ChunkCount = null,
    long? ImpassableCellCount = null,
    long? UnbuildableCellCount = null,
    long? SlopeImpassableCellCount = null,
    long? SlopeUnbuildableCellCount = null,
    long? WaterRuleApplicationCount = null,
    long? PassabilityRuleApplicationCount = null,
    long? BuildabilityRuleApplicationCount = null,
    long? PlayableForcedCellCount = null);

internal sealed record PayloadKindCountDto(string? Kind = null, long? Count = null);

internal sealed record PayloadFeaturesSummaryDto(
    long? TotalCount = null,
    List<PayloadKindCountDto>? CountsByKind = null);

internal sealed record PayloadValidationCheckDto(
    string? Category = null,
    string? Name = null,
    string? Status = null,
    double? Expected = null,
    double? Actual = null,
    double? Tolerance = null,
    string? Units = null,
    string? Message = null);

internal sealed record PayloadReportDto(
    string? Kind = null,
    int? SchemaVersion = null,
    List<PayloadDiagnosticDto>? Diagnostics = null,
    PayloadTerrainDto? Terrain = null,
    PayloadImageryDto? Imagery = null,
    PayloadMaskDto? Masks = null,
    PayloadFeaturesSummaryDto? Features = null,
    List<PayloadValidationCheckDto>? ValidationChecks = null,
    string? PayloadSha256 = null);
