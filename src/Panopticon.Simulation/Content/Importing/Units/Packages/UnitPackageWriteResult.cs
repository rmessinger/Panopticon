#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>One write finding. Codes are stable identifiers from <see cref="UnitPackageWriteDiagnosticCodes"/>.</summary>
public sealed record UnitPackageWriteDiagnostic(string Code, string Message, ContentId? Subject = null)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Outcome of one unit package write attempt. Success names the committed package root.</summary>
public sealed record UnitPackageWriteResult(bool Success, IReadOnlyList<UnitPackageWriteDiagnostic> Diagnostics, string? PackageRoot = null);
