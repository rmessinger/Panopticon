#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Outcome of one chunk file read: a typed payload on success, or diagnostics and no payload on rejection.</summary>
public sealed record MapPackageChunkReadResult<T>(T? Payload, IReadOnlyList<MapPackageDiagnostic> Diagnostics)
    where T : class
{
    [MemberNotNullWhen(true, nameof(Payload))]
    public bool Success => Payload is not null && Diagnostics.Count == 0;
}

/// <summary>Outcome of one whole-package write attempt with the diagnostics collected along the way.</summary>
public sealed record MapPackageWriteResult(bool Success, IReadOnlyList<MapPackageDiagnostic> Diagnostics);
