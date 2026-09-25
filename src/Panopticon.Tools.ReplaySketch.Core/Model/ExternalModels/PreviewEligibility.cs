#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Whether a preview can run: no error-severity diagnostics and no missing capabilities.
/// Warnings alone never block.
/// </summary>
public sealed record PreviewEligibility(
    bool CanRender,
    IReadOnlyList<string> BlockingDiagnosticCodes,
    IReadOnlyList<string> MissingCapabilities)
{
    public static PreviewEligibility NotEvaluated { get; } = new(
        false,
        [],
        []);

    public static PreviewEligibility Evaluate(
        IEnumerable<ExternalModelDiagnostic> diagnostics,
        IEnumerable<string>? missingCapabilities = null)
    {
        if (diagnostics is null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        var blockingCodes = diagnostics
            .Where(d => d.Severity == ExternalDiagnosticSeverity.Error)
            .Select(d => d.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        var capabilities = missingCapabilities?.ToArray() ?? [];

        return new PreviewEligibility(
            blockingCodes.Length == 0 && capabilities.Length == 0,
            blockingCodes,
            capabilities);
    }
}
