using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Explicit conversion between the internal report DTOs and the immutable report
/// models, plus the deterministic streaming JSON writer for <c>report.json</c>.
/// </summary>
internal static class PayloadReportJson
{
    internal static GeneratedMapPayloadReport Build(PayloadReportDto dto)
    {
        var kind = RequireString(dto.Kind, "kind");
        if (kind != GeneratedMapPayloadReport.ReportKind)
        {
            throw Schema($"The report kind '{kind}' is not '{GeneratedMapPayloadReport.ReportKind}'.");
        }

        var schemaVersion = Require(dto.SchemaVersion, "schemaVersion");
        if (schemaVersion != GeneratedMapPayloadReport.CurrentSchemaVersion)
        {
            throw Schema(
                $"The schema version {schemaVersion} is unsupported; this reader supports version {GeneratedMapPayloadReport.CurrentSchemaVersion}.");
        }

        var diagnostics = new List<PayloadDiagnostic>();

        foreach (var diagnostic in Require(dto.Diagnostics, "diagnostics"))
        {
            diagnostics.Add(
                new PayloadDiagnostic(
                    RequireString(diagnostic.Code, "diagnostics.code"),
                    ParseEnum<GeoStage>(diagnostic.Stage, "diagnostics.stage"),
                    ParseEnum<GeoSeverity>(diagnostic.Severity, "diagnostics.severity"),
                    RequireString(diagnostic.Message, "diagnostics.message"),
                    diagnostic.SourcePath));
        }

        var terrainDto = Require(dto.Terrain, "terrain");
        var terrain = new PayloadTerrainGenerationSummary(
            BuildSampling(Require(terrainDto.Sampling, "terrain.sampling"), "terrain.sampling"),
            Require(terrainDto.ChunkCount, "terrain.chunkCount"),
            terrainDto.MinHeight,
            terrainDto.MaxHeight);

        PayloadImageryGenerationSummary? imagery = null;
        if (dto.Imagery is { } imageryDto)
        {
            imagery = new PayloadImageryGenerationSummary(
                BuildSampling(Require(imageryDto.Sampling, "imagery.sampling"), "imagery.sampling"),
                Require(imageryDto.ChunkCount, "imagery.chunkCount"));
        }

        var masksDto = Require(dto.Masks, "masks");
        var masks = new PayloadMaskGenerationSummary(
            Require(masksDto.ImpassableThresholdDegrees, "masks.impassableThresholdDegrees"),
            Require(masksDto.UnbuildableThresholdDegrees, "masks.unbuildableThresholdDegrees"),
            Require(masksDto.VerticalExaggeration, "masks.verticalExaggeration"),
            Require(masksDto.ChunkCount, "masks.chunkCount"),
            Require(masksDto.ImpassableCellCount, "masks.impassableCellCount"),
            Require(masksDto.UnbuildableCellCount, "masks.unbuildableCellCount"),
            Require(masksDto.SlopeImpassableCellCount, "masks.slopeImpassableCellCount"),
            Require(masksDto.SlopeUnbuildableCellCount, "masks.slopeUnbuildableCellCount"),
            Require(masksDto.WaterRuleApplicationCount, "masks.waterRuleApplicationCount"),
            Require(masksDto.PassabilityRuleApplicationCount, "masks.passabilityRuleApplicationCount"),
            Require(masksDto.BuildabilityRuleApplicationCount, "masks.buildabilityRuleApplicationCount"),
            Require(masksDto.PlayableForcedCellCount, "masks.playableForcedCellCount"));

        var featuresDto = Require(dto.Features, "features");
        var features = new PayloadFeatureGenerationSummary(
            Require(featuresDto.TotalCount, "features.totalCount"),
            BuildKindCounts(Require(featuresDto.CountsByKind, "features.countsByKind")));

        var checks = new List<PayloadValidationCheck>();

        foreach (var check in Require(dto.ValidationChecks, "validationChecks"))
        {
            checks.Add(
                new PayloadValidationCheck(
                    RequireString(check.Category, "validationChecks.category"),
                    RequireString(check.Name, "validationChecks.name"),
                    ParseEnum<PayloadValidationStatus>(check.Status, "validationChecks.status"),
                    check.Expected,
                    check.Actual,
                    check.Tolerance,
                    check.Units,
                    check.Message));
        }

        PayloadSamplingSummary? classification = null;
        if (dto.ClassificationSampling is { } classificationDto)
        {
            classification = BuildSampling(classificationDto, "classificationSampling");
        }

        return new GeneratedMapPayloadReport(
            diagnostics,
            terrain,
            masks,
            features,
            checks,
            RequireString(dto.PayloadSha256, "payloadSha256"),
            imagery,
            classification);
    }

    internal static void Write(Utf8JsonWriter writer, GeneratedMapPayloadReport report, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", report.Kind);
        writer.WriteNumber("schemaVersion", report.SchemaVersion);

        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();

        foreach (var diagnostic in report.Diagnostics)
        {
            context.Checkpoint(writer);
            WriteDiagnostic(writer, diagnostic, context);
        }

        writer.WriteEndArray();

        context.Checkpoint(writer);

        writer.WritePropertyName("terrain");
        writer.WriteStartObject();
        WriteSampling(writer, report.Terrain.Sampling);
        writer.WriteNumber("chunkCount", report.Terrain.ChunkCount);

        if (report.Terrain.MinHeightGameUnits is { } minHeight)
        {
            writer.WriteNumber("minHeightGameUnits", minHeight);
        }

        if (report.Terrain.MaxHeightGameUnits is { } maxHeight)
        {
            writer.WriteNumber("maxHeightGameUnits", maxHeight);
        }

        writer.WriteEndObject();

        if (report.Imagery is { } imagery)
        {
            writer.WritePropertyName("imagery");
            writer.WriteStartObject();
            WriteSampling(writer, imagery.Sampling);
            writer.WriteNumber("chunkCount", imagery.ChunkCount);
            writer.WriteEndObject();
        }

        if (report.ClassificationSampling is { } classification)
        {
            context.Checkpoint(writer);

            writer.WritePropertyName("classificationSampling");
            writer.WriteStartObject();
            WriteSamplingProperties(writer, classification);
            writer.WriteEndObject();
        }

        var masks = report.Masks;
        writer.WritePropertyName("masks");
        writer.WriteStartObject();
        writer.WriteNumber("impassableThresholdDegrees", masks.ImpassableThresholdDegrees);
        writer.WriteNumber("unbuildableThresholdDegrees", masks.UnbuildableThresholdDegrees);
        writer.WriteNumber("verticalExaggeration", masks.VerticalExaggeration);
        writer.WriteNumber("chunkCount", masks.ChunkCount);
        writer.WriteNumber("impassableCellCount", masks.ImpassableCellCount);
        writer.WriteNumber("unbuildableCellCount", masks.UnbuildableCellCount);
        writer.WriteNumber("slopeImpassableCellCount", masks.SlopeImpassableCellCount);
        writer.WriteNumber("slopeUnbuildableCellCount", masks.SlopeUnbuildableCellCount);
        writer.WriteNumber("waterRuleApplicationCount", masks.WaterRuleApplicationCount);
        writer.WriteNumber("passabilityRuleApplicationCount", masks.PassabilityRuleApplicationCount);
        writer.WriteNumber("buildabilityRuleApplicationCount", masks.BuildabilityRuleApplicationCount);
        writer.WriteNumber("playableForcedCellCount", masks.PlayableForcedCellCount);
        writer.WriteEndObject();

        var features = report.Features;
        writer.WritePropertyName("features");
        writer.WriteStartObject();
        writer.WriteNumber("totalCount", features.TotalCount);

        writer.WritePropertyName("countsByKind");
        writer.WriteStartArray();

        foreach (var kindCount in features.CountsByKind)
        {
            context.Checkpoint(writer);

            writer.WriteStartObject();
            writer.WriteString("kind", PayloadEnumTokens<GeneratedFeatureKind>.ToToken(kindCount.Kind));
            writer.WriteNumber("count", kindCount.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("validationChecks");
        writer.WriteStartArray();

        foreach (var check in report.ValidationChecks)
        {
            context.Checkpoint(writer);
            WriteValidationCheck(writer, check, context);
        }

        writer.WriteEndArray();

        context.EnsureStringWithinBudget(writer, report.PayloadSha256Hex);
        writer.WriteString("payloadSha256", report.PayloadSha256Hex);

        writer.WriteEndObject();
    }

    private static PayloadSamplingSummary BuildSampling(PayloadSamplingDto sampling, string member)
    {
        var kernels = Require(sampling.KernelCounts, member + ".kernelCounts");
        if (kernels.Count != Enum.GetValues<SampleKernel>().Length)
        {
            throw Schema($"{member}.kernelCounts must hold exactly one entry per kernel.");
        }

        var kernelCounts = new List<PayloadKernelSampleCount>(kernels.Count);
        for (var index = 0; index < kernels.Count; index++)
        {
            var entry = kernels[index];
            var kernel = ParseEnum<SampleKernel>(entry.Kernel, member + ".kernelCounts.kernel");

            if (index > 0 && (int)kernel <= (int)kernelCounts[index - 1].Kernel)
            {
                throw Schema($"{member}.kernelCounts must list every kernel exactly once, in enum order.");
            }

            kernelCounts.Add(new PayloadKernelSampleCount(kernel, Require(entry.Count, member + ".kernelCounts.count")));
        }

        return new PayloadSamplingSummary(
            Require(sampling.SampleCount, member + ".sampleCount"),
            Require(sampling.FailedSampleCount, member + ".failedSampleCount"),
            Require(sampling.NoDataFillCount, member + ".noDataFillCount"),
            Require(sampling.OutsideFillCount, member + ".outsideFillCount"),
            kernelCounts,
            BuildObservedRanges(sampling.ObservedRanges, member));
    }

    private static PayloadObservedRanges? BuildObservedRanges(PayloadObservedRangesDto? ranges, string member)
    {
        if (ranges is null)
        {
            return null;
        }

        return new PayloadObservedRanges(
            BuildRange(ranges.SourceGsdXMeters, member + ".sourceGsdXMeters"),
            BuildRange(ranges.SourceGsdYMeters, member + ".sourceGsdYMeters"),
            BuildRange(ranges.OutputSpacingXMeters, member + ".outputSpacingXMeters"),
            BuildRange(ranges.OutputSpacingYMeters, member + ".outputSpacingYMeters"),
            BuildRange(ranges.GsdRatioX, member + ".gsdRatioX"),
            BuildRange(ranges.GsdRatioY, member + ".gsdRatioY"));
    }

    private static PayloadObservedRange? BuildRange(PayloadRangeDto? range, string member)
    {
        if (range is null)
        {
            return null;
        }

        return new PayloadObservedRange(
            Require(range.Min, member + ".min"),
            Require(range.Max, member + ".max"));
    }

    private static List<PayloadFeatureKindCount> BuildKindCounts(List<PayloadKindCountDto> counts)
    {
        if (counts.Count != Enum.GetValues<GeneratedFeatureKind>().Length)
        {
            throw Schema("features.countsByKind must hold exactly one entry per feature kind.");
        }

        var result = new List<PayloadFeatureKindCount>(counts.Count);

        for (var index = 0; index < counts.Count; index++)
        {
            var entry = counts[index];
            var kind = ParseEnum<GeneratedFeatureKind>(entry.Kind, "features.countsByKind.kind");

            if (index > 0 && (int)kind <= (int)result[index - 1].Kind)
            {
                throw Schema("features.countsByKind must list every feature kind exactly once, in enum order.");
            }

            result.Add(new PayloadFeatureKindCount(kind, Require(entry.Count, "features.countsByKind.count")));
        }

        return result;
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

    private static void WriteDiagnostic(Utf8JsonWriter writer, PayloadDiagnostic diagnostic, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        context.EnsureStringWithinBudget(writer, diagnostic.Code);
        writer.WriteString("code", diagnostic.Code);
        writer.WriteString("stage", PayloadEnumTokens<GeoStage>.ToToken(diagnostic.Stage));
        writer.WriteString("severity", PayloadEnumTokens<GeoSeverity>.ToToken(diagnostic.Severity));
        context.EnsureStringWithinBudget(writer, diagnostic.Message);
        writer.WriteString("message", diagnostic.Message);

        if (diagnostic.SourcePath is { } sourcePath)
        {
            context.EnsureStringWithinBudget(writer, sourcePath);
            writer.WriteString("sourcePath", sourcePath);
        }

        writer.WriteEndObject();
    }

    private static void WriteSampling(Utf8JsonWriter writer, PayloadSamplingSummary sampling)
    {
        writer.WritePropertyName("sampling");
        writer.WriteStartObject();
        WriteSamplingProperties(writer, sampling);
        writer.WriteEndObject();
    }

    /// <summary>Writes the sampling members into an already open summary object.</summary>
    private static void WriteSamplingProperties(Utf8JsonWriter writer, PayloadSamplingSummary sampling)
    {
        writer.WriteNumber("sampleCount", sampling.SampleCount);
        writer.WriteNumber("failedSampleCount", sampling.FailedSampleCount);
        writer.WriteNumber("noDataFillCount", sampling.NoDataFillCount);
        writer.WriteNumber("outsideFillCount", sampling.OutsideFillCount);

        writer.WritePropertyName("kernelCounts");
        writer.WriteStartArray();

        foreach (var entry in sampling.KernelCounts)
        {
            writer.WriteStartObject();
            writer.WriteString("kernel", PayloadEnumTokens<SampleKernel>.ToToken(entry.Kernel));
            writer.WriteNumber("count", entry.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if (sampling.ObservedRanges is { } ranges)
        {
            writer.WritePropertyName("observedRanges");
            writer.WriteStartObject();
            WriteRange(writer, "sourceGsdXMeters", ranges.SourceGsdXMeters);
            WriteRange(writer, "sourceGsdYMeters", ranges.SourceGsdYMeters);
            WriteRange(writer, "outputSpacingXMeters", ranges.OutputSpacingXMeters);
            WriteRange(writer, "outputSpacingYMeters", ranges.OutputSpacingYMeters);
            WriteRange(writer, "gsdRatioX", ranges.GsdRatioX);
            WriteRange(writer, "gsdRatioY", ranges.GsdRatioY);
            writer.WriteEndObject();
        }
    }

    private static void WriteRange(Utf8JsonWriter writer, string member, PayloadObservedRange? range)
    {
        if (range is not { } value)
        {
            return;
        }

        writer.WritePropertyName(member);
        writer.WriteStartObject();
        writer.WriteNumber("min", value.Min);
        writer.WriteNumber("max", value.Max);
        writer.WriteEndObject();
    }

    private static void WriteValidationCheck(Utf8JsonWriter writer, PayloadValidationCheck check, PayloadWriteContext context)
    {
        writer.WriteStartObject();
        context.EnsureStringWithinBudget(writer, check.Category);
        writer.WriteString("category", check.Category);
        context.EnsureStringWithinBudget(writer, check.Name);
        writer.WriteString("name", check.Name);
        writer.WriteString("status", PayloadEnumTokens<PayloadValidationStatus>.ToToken(check.Status));

        if (check.Expected is { } expected)
        {
            writer.WriteNumber("expected", expected);
        }

        if (check.Actual is { } actual)
        {
            writer.WriteNumber("actual", actual);
        }

        if (check.Tolerance is { } tolerance)
        {
            writer.WriteNumber("tolerance", tolerance);
        }

        if (check.Units is { } units)
        {
            context.EnsureStringWithinBudget(writer, units);
            writer.WriteString("units", units);
        }

        if (check.Message is { } message)
        {
            context.EnsureStringWithinBudget(writer, message);
            writer.WriteString("message", message);
        }

        writer.WriteEndObject();
    }
}
