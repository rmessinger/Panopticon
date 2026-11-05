using System;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Serialized form of one diagnostic: no exception detail and no wall-clock value. The
/// optional source path is a normalized manifest-relative path.
/// </summary>
public sealed record PayloadDiagnostic
{
    public PayloadDiagnostic(
        string code,
        GeoStage stage,
        GeoSeverity severity,
        string message,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Undefined diagnostic stage.");
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "Undefined diagnostic severity.");
        }

        ArgumentException.ThrowIfNullOrEmpty(message);
        if (sourcePath is not null)
        {
            PayloadValidationRules.NormalizedRelativePath(sourcePath, nameof(sourcePath));
        }

        Code = code;
        Stage = stage;
        Severity = severity;
        Message = message;
        SourcePath = sourcePath;
    }

    public string Code { get; }

    public GeoStage Stage { get; }

    public GeoSeverity Severity { get; }

    public string Message { get; }

    /// <summary>Normalized manifest-relative path of the affected input, when known.</summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Converts a runtime diagnostic, dropping its exception detail. The source path must
    /// already be normalized; absolute paths are rejected instead of being rewritten.
    /// </summary>
    public static PayloadDiagnostic FromGeoDiagnostic(GeoDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new PayloadDiagnostic(
            diagnostic.Code,
            diagnostic.Stage,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.SourcePath);
    }
}
