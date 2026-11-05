using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Stream-based entry points of the payload JSON codec. Reads are single-pass,
/// incremental, and byte-bounded, never seek, and never buffer the whole document;
/// recoverable failures become typed diagnostics. Writes stream through
/// <see cref="Utf8JsonWriter"/> onto a bounded wrapper with incremental flushes.
/// </summary>
internal static class PayloadJson
{
    internal static GeneratedMapPayloadMetadata? TryReadMetadata(
        Stream stream,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(limits);

        try
        {
            var context = new PayloadParseContext(limits, cancellationToken);
            var dto = PayloadJsonReader.ParseMetadata(stream, context);

            return PayloadMetadataJson.Build(dto);
        }
        catch (Exception ex) when (ex is PayloadJsonSchemaException or JsonException)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadSchemaInvalid, GeoStage.Parse, $"payload.json is invalid: {ex.Message}"));
            return null;
        }
        catch (PayloadJsonLimitException ex)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadLimitExceeded, GeoStage.Parse, ex.Message));
            return null;
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadValueInvalid, GeoStage.Validate, $"payload.json content is invalid: {ex.Message}"));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadResourceFailure, GeoStage.Parse, $"payload.json could not be read: {ex.Message}"));
            return null;
        }
    }

    internal static GeneratedMapPayloadReport? TryReadReport(
        Stream stream,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(limits);

        try
        {
            var context = new PayloadParseContext(limits, cancellationToken);
            var dto = PayloadJsonReader.ParseReport(stream, context);

            return PayloadReportJson.Build(dto);
        }
        catch (Exception ex) when (ex is PayloadJsonSchemaException or JsonException)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadSchemaInvalid, GeoStage.Parse, $"report.json is invalid: {ex.Message}"));
            return null;
        }
        catch (PayloadJsonLimitException ex)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadLimitExceeded, GeoStage.Parse, ex.Message));
            return null;
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadValueInvalid, GeoStage.Validate, $"report.json content is invalid: {ex.Message}"));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics.Add(Error(PayloadDiagnosticCodes.PayloadResourceFailure, GeoStage.Parse, $"report.json could not be read: {ex.Message}"));
            return null;
        }
    }

    internal static void WriteMetadata(
        Stream stream,
        GeneratedMapPayloadMetadata metadata,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(metadata);

        var maxBytes = (limits ?? new GeospatialImportLimits()).MaxPayloadMetadataBytes;
        using var bounded = new BoundedWriteStream(stream, maxBytes);
        using var writer = new Utf8JsonWriter(bounded);
        PayloadMetadataJson.Write(writer, metadata, new PayloadWriteContext(maxBytes, cancellationToken));
    }

    internal static void WriteReport(
        Stream stream,
        GeneratedMapPayloadReport report,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(report);

        var maxBytes = (limits ?? new GeospatialImportLimits()).MaxPayloadMetadataBytes;
        using var bounded = new BoundedWriteStream(stream, maxBytes);
        using var writer = new Utf8JsonWriter(bounded);
        PayloadReportJson.Write(writer, report, new PayloadWriteContext(maxBytes, cancellationToken));
    }

    private static GeoDiagnostic Error(string code, GeoStage stage, string message)
    {
        return new GeoDiagnostic(code, stage, GeoSeverity.Error, message);
    }
}
