#nullable enable

namespace Panopticon.Content.Importing.Units;

/// <summary>One import finding. Codes are stable identifiers for tests and reports.</summary>
public sealed record ImportDiagnostic(
    ImportDiagnosticSeverity Severity,
    ImportStage Stage,
    string Code,
    string Message,
    ContentId? Subject = null,
    Provenance? Provenance = null)
{
    public bool IsError => Severity == ImportDiagnosticSeverity.Error;

    public override string ToString() => $"{Severity} {Stage} {Code}: {Message}";
}
