#nullable enable

namespace OpenSage.Content.Importing;

/// <summary>Severity of a non-fatal inspection note. Fatal failures throw instead.</summary>
public enum ExternalInspectionDiagnosticSeverity
{
    Info,

    Warning,
}

/// <summary>
/// One non-fatal parser note surfaced by inspection. Engine-owned values only; no parser
/// objects. Fatal problems throw the same exceptions the importers throw.
/// </summary>
public sealed record ExternalAssetInspectionDiagnostic(
    string Code,
    ExternalInspectionDiagnosticSeverity Severity,
    string Message);
