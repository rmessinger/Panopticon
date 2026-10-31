using System;

namespace Panopticon.Geospatial;

public sealed record GeoDiagnostic
{
    public string Code { get; }

    public GeoStage Stage { get; }

    public GeoSeverity Severity { get; }

    public string Message { get; }

    public string? SourcePath { get; }

    public Exception? Detail { get; }

    public GeoDiagnostic(
        string code,
        GeoStage stage,
        GeoSeverity severity,
        string message,
        string? sourcePath = null,
        Exception? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);

        Code = code;
        Stage = stage;
        Severity = severity;
        Message = message;
        SourcePath = sourcePath;
        Detail = detail;
    }
}
