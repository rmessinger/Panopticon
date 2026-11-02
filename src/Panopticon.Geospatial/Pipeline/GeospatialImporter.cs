using System;
using System.Collections.Generic;
using System.Threading;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.Generation;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Pipeline.ImportOrchestration;
using Panopticon.Geospatial.Pipeline.Reporting;
using Panopticon.Geospatial.Pipeline.SourceLoading;

namespace Panopticon.Geospatial.Pipeline;

/// <summary>
/// WI-B4b-18 entry point: one transactional standalone payload import under the R2
/// contract (docs/roadmap/implementation/track-r-r2-payload.md). Loads and hashes the
/// confined local sources, plans the grid, streams chunks through the writer's exact
/// staged-file helper, writes payload.json plus the initial report, runs the ordered
/// staged validators against one stage reader when any are declared, rewrites the
/// report, and commits through the writer's final full reader validation. The result is
/// constructed once after the writer is disposed, so runtime diagnostics include
/// cleanup warnings; cancellation and unexpected validator exceptions propagate after
/// that cleanup. No validation or report rebuild happens after the critical rename.
/// </summary>
public static class GeospatialImporter
{
    public static GeoImportResult Run(string manifestPath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        return Run(manifestPath, outputDirectory, new GeoImportOptions(), cancellationToken);
    }

    public static GeoImportResult Run(
        string manifestPath,
        string outputDirectory,
        GeoImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new GeoDiagnosticBag();
        var outcome = RunStaged(manifestPath, outputDirectory, options, diagnostics, cancellationToken);

        // Constructed exactly once, after disposal completed the runtime diagnostics.
        return new GeoImportResult(
            outcome.Success,
            diagnostics.Items,
            outcome.Success ? outcome.PayloadPath : null,
            outcome.RetainedBackupPath,
            outcome.Report);
    }

    private static ImportRunOutcome RunStaged(
        string manifestPath,
        string outputDirectory,
        GeoImportOptions options,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        GeneratedMapPayloadWriter? writer = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sources = ImportSourceLoader.Load(manifestPath, diagnostics, options.Limits, cancellationToken);

            if (sources is null)
            {
                return ImportRunOutcome.Failed;
            }

            var plan = ImportGenerationPlan.Create(sources, diagnostics, options.Limits, cancellationToken);

            if (plan is null)
            {
                return ImportRunOutcome.Failed;
            }

            // ProtectedInputPaths already covers the manifest and every resolved source.
            writer = GeneratedMapPayloadWriter.Create(
                outputDirectory,
                sources.ProtectedInputPaths,
                diagnostics,
                options.Force,
                options.Limits,
                cancellationToken);

            if (writer is null)
            {
                return ImportRunOutcome.Failed;
            }

            var output = ImportChunkGeneration.Run(
                plan, writer.StagedFiles, writer.StageDirectory, diagnostics, options.Limits, cancellationToken);

            if (output is null)
            {
                return ImportRunOutcome.Failed;
            }

            var payloadSha256Hex = writer.WriteMetadata(output.Metadata, cancellationToken);

            if (payloadSha256Hex is null)
            {
                return ImportRunOutcome.Failed;
            }

            var report = ImportReportBuilder.Build(
                output,
                plan,
                payloadSha256Hex,
                diagnostics.Items,
                Array.Empty<PayloadValidationCheck>(),
                [writer.StageDirectory, writer.OutputDirectory]);

            if (!writer.WriteReport(report, cancellationToken))
            {
                return ImportRunOutcome.Failed;
            }

            if (options.StagedValidators.Count > 0)
            {
                report = RunStagedValidators(writer, sources, output, plan, payloadSha256Hex, options, diagnostics, cancellationToken);

                if (report is null)
                {
                    return ImportRunOutcome.Failed;
                }
            }

            if (!writer.TryCommit(cancellationToken))
            {
                // A failed commit may still hold a retained backup or a preserved
                // recovery staging; both are read here while the writer is alive.
                return new ImportRunOutcome(
                    false, null, writer.RetainedBackupPath ?? writer.RecoveryStagePath, report);
            }

            // The committed report object is reused as the result; nothing runs between
            // the successful critical rename and the result construction.
            return new ImportRunOutcome(true, writer.OutputDirectory, writer.RetainedBackupPath, report);
        }
        finally
        {
            // Disposal removes only owned staging and never touches a committed output
            // or retained backup; its warnings land in the runtime diagnostics before
            // the public result is constructed.
            writer?.Dispose();
        }
    }

    /// <summary>
    /// Runs every declared validator in order against one opened stage reader and the
    /// same loaded manifest generation used, bounds the aggregated checks against
    /// maxFeatureCount before any copy, converts failed checks into typed errors, and
    /// rewrites the report. Returns the rewritten snapshot, or null on recoverable
    /// failure. Validator exceptions and cancellation propagate.
    /// </summary>
    private static GeneratedMapPayloadReport? RunStagedValidators(
        GeneratedMapPayloadWriter writer,
        PreparedImportSources sources,
        ImportGenerationOutput output,
        ImportGenerationPlan plan,
        string payloadSha256Hex,
        GeoImportOptions options,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var reader = GeneratedMapPayloadReader.Open(writer.StageDirectory, diagnostics, options.Limits, cancellationToken);

        if (reader is null)
        {
            // The stage failed its first full validation; no validator runs and no
            // rewrite happens, so the failure path is not walked twice.
            return null;
        }

        var aggregated = new List<PayloadValidationCheck>();

        using (reader)
        {
            foreach (var validator in options.StagedValidators)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var checks = validator.Validate(reader, sources.LoadedManifest, diagnostics, cancellationToken);

                // The bound is enforced on the IReadOnlyList counts before any copy, so
                // an oversized validator result is rejected without materializing it twice.
                if (aggregated.Count + checks.Count > options.Limits.MaxFeatureCount)
                {
                    diagnostics.Add(new GeoDiagnostic(
                        ImporterDiagnostics.CheckBudgetExceeded,
                        GeoStage.Validate,
                        GeoSeverity.Error,
                        $"The staged validators produced {aggregated.Count + checks.Count} aggregated validation checks, beyond the maxFeatureCount bound of {options.Limits.MaxFeatureCount}.",
                        writer.OutputDirectory));

                    return null;
                }

                for (var index = 0; index < checks.Count; index++)
                {
                    aggregated.Add(checks[index]);
                }
            }

            // A failed check blocks the commit even when its validator added no error
            // diagnostic; the typed error makes the block explicit in every result.
            foreach (var check in aggregated)
            {
                if (check.Status == PayloadValidationStatus.Failed)
                {
                    diagnostics.Add(new GeoDiagnostic(
                        ImporterDiagnostics.ValidatorCheckFailed,
                        GeoStage.Validate,
                        GeoSeverity.Error,
                        $"The staged validation check '{check.Category}/{check.Name}' failed; failed checks block the commit.",
                        writer.OutputDirectory));
                }
            }
        }

        var rewritten = ImportReportBuilder.Build(
            output,
            plan,
            payloadSha256Hex,
            diagnostics.Items,
            aggregated,
            [writer.StageDirectory, writer.OutputDirectory]);

        if (!writer.WriteReport(rewritten, cancellationToken))
        {
            return null;
        }

        return rewritten;
    }

    /// <summary>Locals-only import outcome; the public result is constructed from it after disposal.</summary>
    private sealed record ImportRunOutcome(bool Success, string? PayloadPath, string? RetainedBackupPath, GeneratedMapPayloadReport? Report)
    {
        public static ImportRunOutcome Failed { get; } = new(false, null, null, null);
    }
}
