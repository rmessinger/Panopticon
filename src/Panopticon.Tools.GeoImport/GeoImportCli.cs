using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// In-process entry of the GeoImport CLI over the card 18 importer and reader:
/// `import --manifest &lt;path&gt; --out &lt;dir&gt; [--force]` and `report --payload &lt;dir&gt;`.
/// Exit codes: 0 success, 1 operational or input failure, 2 usage error. Every
/// diagnostic prints as one line `severity code stage message` with whitespace
/// normalized, so messages containing newlines cannot break the line protocol.
/// Cancellation propagates through the token and never misreports a published payload.
/// </summary>
public static class GeoImportCli
{
    private const int SuccessExit = 0;

    private const int OperationalFailureExit = 1;

    private const int UsageExit = 2;

    public static int Run(string[] arguments, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOut);
        ArgumentNullException.ThrowIfNull(errorOut);

        // HelpWriter null keeps the parser silent; usage handling stays here so the
        // streams under test carry exactly the intended lines.
        var parser = new Parser(with => with.HelpWriter = null);

        return parser
            .ParseArguments<ImportOptions, ReportOptions>(arguments)
            .MapResult(
                (ImportOptions options) => RunImport(options, standardOut, errorOut, cancellationToken),
                (ReportOptions options) => RunReport(options, standardOut, errorOut, cancellationToken),
                _ => UsageFailure(errorOut));
    }

    private static int RunImport(ImportOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        try
        {
            var result = GeospatialImporter.Run(
                options.ManifestPath,
                options.OutputDirectory,
                new GeoImportOptions(force: options.Force),
                cancellationToken);

            WriteDiagnostics(result.Diagnostics, standardOut, errorOut);

            if (!result.Success)
            {
                return OperationalFailureExit;
            }

            standardOut.WriteLine($"published: {NormalizeWhitespace(result.PayloadPath)}");

            if (result.RetainedBackupPath is not null)
            {
                standardOut.WriteLine($"retained backup: {NormalizeWhitespace(result.RetainedBackupPath)}");
            }

            return SuccessExit;
        }
        catch (OperationCanceledException)
        {
            // The importer only cancels before the critical rename, so an unpublished
            // payload is stated factually here.
            errorOut.WriteLine("cancelled: the import was cancelled before the payload was published");
            return OperationalFailureExit;
        }
    }

    private static int RunReport(ReportOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        try
        {
            var diagnostics = new GeoDiagnosticBag();
            var reader = GeneratedMapPayloadReader.Open(options.PayloadDirectory, diagnostics, cancellationToken: cancellationToken);

            if (reader is null)
            {
                WriteDiagnostics(diagnostics.Items, standardOut, errorOut);
                return OperationalFailureExit;
            }

            using (reader)
            {
                WriteSummary(reader.Metadata, reader.Report, standardOut);
            }

            WriteDiagnostics(diagnostics.Items, standardOut, errorOut);
            return SuccessExit;
        }
        catch (OperationCanceledException)
        {
            errorOut.WriteLine("cancelled: the report was cancelled before it was read");
            return OperationalFailureExit;
        }
    }

    /// <summary>One-line summary of the validated payload: identity, chunk counts, features, checks.</summary>
    private static void WriteSummary(GeneratedMapPayloadMetadata metadata, GeneratedMapPayloadReport report, TextWriter standardOut)
    {
        var chunks = metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY;
        string imagery;

        if (metadata.Grid.ImageryPixelsPerSample is { } pixelsPerSample)
        {
            imagery = pixelsPerSample + " pixels per sample";
        }
        else
        {
            imagery = "none";
        }

        standardOut.WriteLine($"payloadSha256: {report.PayloadSha256Hex}");
        standardOut.WriteLine($"chunks: {chunks} ({metadata.Grid.ChunksX} x {metadata.Grid.ChunksY})");
        standardOut.WriteLine($"imagery: {imagery}");
        standardOut.WriteLine($"features: {metadata.Features.Count}");
        standardOut.WriteLine($"palette: {metadata.Palette.Count} materials");

        var passed = report.ValidationChecks.Count(check => check.Status == PayloadValidationStatus.Passed);
        var failed = report.ValidationChecks.Count(check => check.Status == PayloadValidationStatus.Failed);
        var notEvaluated = report.ValidationChecks.Count(check => check.Status == PayloadValidationStatus.NotEvaluated);

        standardOut.WriteLine($"checks: {passed} passed, {failed} failed, {notEvaluated} not evaluated");
    }

    private static void WriteDiagnostics(IReadOnlyList<GeoDiagnostic> diagnostics, TextWriter standardOut, TextWriter errorOut)
    {
        foreach (var diagnostic in diagnostics)
        {
            var line = FormatDiagnostic(diagnostic);

            if (diagnostic.Severity == GeoSeverity.Error)
            {
                errorOut.WriteLine(line);
            }
            else
            {
                standardOut.WriteLine(line);
            }
        }
    }

    internal static string FormatDiagnostic(GeoDiagnostic diagnostic)
    {
        return $"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Stage} {NormalizeWhitespace(diagnostic.Message)}";
    }

    /// <summary>Collapses every whitespace run, newlines included, into single spaces.</summary>
    internal static string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static int UsageFailure(TextWriter errorOut)
    {
        errorOut.WriteLine(
            NormalizeWhitespace(
                "usage: geoimport import --manifest <path> --out <dir> [--force]; geoimport report --payload <dir>"));
        return UsageExit;
    }

    [Verb("import", HelpText = "Runs one transactional standalone payload import.")]
    public sealed class ImportOptions
    {
        [Option('m', "manifest", Required = true, HelpText = "Path to the import manifest.")]
        public string ManifestPath { get; set; } = null!;

        [Option('o', "out", Required = true, HelpText = "Output payload directory.")]
        public string OutputDirectory { get; set; } = null!;

        [Option('f', "force", HelpText = "Replace an existing output directory, always retaining a backup.")]
        public bool Force { get; set; }
    }

    [Verb("report", HelpText = "Prints a summary of one validated payload directory.")]
    public sealed class ReportOptions
    {
        [Option('p', "payload", Required = true, HelpText = "Path to the payload directory.")]
        public string PayloadDirectory { get; set; } = null!;
    }
}
