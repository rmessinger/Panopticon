using System;
using System.Collections.Generic;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.Generation;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;

namespace Panopticon.Geospatial.Pipeline.Reporting;

/// <summary>
/// Pure assembly of one <c>report.json</c> model over the accepted generation output
/// (R2 contract): converts the runtime diagnostic bag and the ordered staged validation
/// checks into their serialized forms, bound to the SHA-256 of the exact
/// <c>payload.json</c> bytes that were written. Terrain, imagery, and classification
/// sampling summaries stay separate. Source paths and known paths inside message text
/// are normalized against the same loaded manifest and source catalog the plan carries,
/// and owned output and staging directory prefixes collapse to the stable
/// '<payload>' placeholder, so identical inputs serialize identically across output
/// and staging GUIDs. The runtime bag is read, never mutated; exception details are
/// never traversed or serialized; error severities and failed check statuses pass
/// through unchanged for the writer's commit gate. No I/O, no chunk generation, no
/// validator invocation, no clocks, and no new absolute paths.
/// </summary>
internal static class ImportReportBuilder
{
    /// <summary>Stable placeholder every owned output and staging directory maps to.</summary>
    internal const string OwnedDirectoryPlaceholder = ImportReportPathNormalizer.Placeholder;

    public static GeneratedMapPayloadReport Build(
        ImportGenerationOutput output,
        ImportGenerationPlan plan,
        string payloadSha256Hex,
        IReadOnlyList<GeoDiagnostic> diagnostics,
        IReadOnlyList<PayloadValidationCheck> validationChecks,
        IReadOnlyList<string>? ownedDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(payloadSha256Hex);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(validationChecks);

        var normalizer = ImportReportPathNormalizer.Create(
            plan.Sources.Sources,
            plan.Sources.ProtectedInputPaths,
            ownedDirectories);

        var payloadDiagnostics = new PayloadDiagnostic[diagnostics.Count];

        for (var index = 0; index < diagnostics.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(diagnostics[index]);
            payloadDiagnostics[index] = ToPayloadDiagnostic(normalizer, diagnostics[index]);
        }

        var checks = new PayloadValidationCheck[validationChecks.Count];

        for (var index = 0; index < validationChecks.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(validationChecks[index]);
            checks[index] = WithNormalizedMessage(normalizer, validationChecks[index]);
        }

        return new GeneratedMapPayloadReport(
            payloadDiagnostics,
            output.Terrain,
            output.Masks,
            output.Features,
            checks,
            payloadSha256Hex,
            output.Imagery,
            output.ClassificationSampling);
    }

    private static PayloadDiagnostic ToPayloadDiagnostic(ImportReportPathNormalizer normalizer, GeoDiagnostic runtime)
    {
        // Rebuilt without Detail so the exception graph is never traversed or
        // serialized; code, stage, and severity validation stay with the accepted model
        // and invalid values propagate.
        return PayloadDiagnostic.FromGeoDiagnostic(
            new GeoDiagnostic(
                runtime.Code,
                runtime.Stage,
                runtime.Severity,
                normalizer.NormalizeText(runtime.Message),
                normalizer.NormalizeSourcePath(runtime.SourcePath)));
    }

    private static PayloadValidationCheck WithNormalizedMessage(
        ImportReportPathNormalizer normalizer,
        PayloadValidationCheck check)
    {
        if (check.Message is null)
        {
            return check;
        }

        return new PayloadValidationCheck(
            check.Category,
            check.Name,
            check.Status,
            check.Expected,
            check.Actual,
            check.Tolerance,
            check.Units,
            normalizer.NormalizeText(check.Message));
    }
}
