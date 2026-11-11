using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Preview;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// In-process entry of the GeoImport CLI over the card 18 importer and reader:
/// `import --manifest &lt;path&gt; --out &lt;dir&gt; [--force]`, `report --payload &lt;dir&gt;`,
/// `preview --payload &lt;dir&gt; --textures &lt;map.json&gt; [--crop cx0,cy0,cx1,cy1]
/// [--force] --out &lt;maps dir&gt;`, and `package --payload &lt;dir&gt; --out &lt;package dir&gt;
/// [--name &lt;map name&gt;] [--force]`. Exit codes: 0 success, 1 operational or input failure,
/// 2 usage error. Every diagnostic prints as one line `severity code stage message` with
/// whitespace normalized, so messages containing newlines cannot break the line protocol.
/// Cancellation propagates through the token and never misreports a published payload.
/// </summary>
public static class GeoImportCli
{
    private const int SuccessExit = 0;

    private const int OperationalFailureExit = 1;

    private const int UsageExit = 2;

    private const int TexturesBlockBytes = 64 * 1024;

    /// <summary>CLI-private encoded UTF-8 bound on the textures file, enforced while streaming (card 24 decision 12).</summary>
    internal const int MaxTexturesBytes = 1024 * 1024;

    public static int Run(string[] arguments, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        return Run(arguments, standardOut, errorOut, cancellationToken, null);
    }

    public static int Run(
        string[] arguments,
        TextWriter standardOut,
        TextWriter errorOut,
        CancellationToken cancellationToken,
        GeoImportCliOptions? options)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOut);
        ArgumentNullException.ThrowIfNull(errorOut);

        // HelpWriter null keeps the parser silent; usage handling stays here so the
        // streams under test carry exactly the intended lines.
        var parser = new Parser(with => with.HelpWriter = null);

        return parser
            .ParseArguments<ImportOptions, ReportOptions, PreviewOptions, PackageOptions>(arguments)
            .MapResult(
                (ImportOptions importOptions) => RunImport(importOptions, standardOut, errorOut, cancellationToken),
                (ReportOptions reportOptions) => RunReport(reportOptions, standardOut, errorOut, cancellationToken),
                (PreviewOptions previewOptions) => RunPreview(previewOptions, options, standardOut, errorOut, cancellationToken),
                (PackageOptions packageOptions) => RunPackage(packageOptions, standardOut, errorOut, cancellationToken),
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

    /// <summary>
    /// The preview verb: converts an already published payload into a legacy `.map` in the
    /// maps directory, through its own publication transaction. The payload itself is an
    /// unmodified read input; preview failures and cancellation never claim the payload was
    /// not published. Argument syntax problems (including a malformed crop) exit 2; content,
    /// IO, catalog, build, and publication failures exit 1.
    /// </summary>
    private static int RunPreview(
        PreviewOptions options,
        GeoImportCliOptions? cliOptions,
        TextWriter standardOut,
        TextWriter errorOut,
        CancellationToken cancellationToken)
    {
        PreviewCrop? crop = null;

        if (options.Crop is not null && !TryParseCrop(options.Crop, out crop))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: geoimport preview --payload <dir> --textures <map.json> [--crop cx0,cy0,cx1,cy1] [--force] --out <maps dir> (crop must be exactly four integers cx0,cy0,cx1,cy1)"));
            return UsageExit;
        }

        string payloadRoot = options.PayloadDirectory;

        try
        {
            var diagnostics = new GeoDiagnosticBag();

            var texturesJson = TryReadTextures(options.TexturesPath, diagnostics, cancellationToken);

            if (texturesJson is null)
            {
                return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
            }

            var transaction = PreviewMapTransaction.Create(
                options.PayloadDirectory, options.TexturesPath, options.MapsDirectory, options.Force, diagnostics);

            if (transaction is null)
            {
                return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
            }

            cliOptions?.ConfigureTransaction?.Invoke(transaction);

            payloadRoot = transaction.PayloadRoot;

            var catalog = TryResolveCatalog(cliOptions, diagnostics);

            if (catalog is null)
            {
                return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
            }

            var textureMap = PreviewTextureMap.FromJson(texturesJson, catalog, diagnostics);

            if (textureMap is null)
            {
                return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
            }

            var reader = GeneratedMapPayloadReader.Open(payloadRoot, diagnostics, cancellationToken: cancellationToken);

            if (reader is null)
            {
                return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
            }

            using (reader)
            using (transaction)
            {
                var build = GeneratedMapPreviewBuilder.Build(reader, textureMap, crop);

                WriteDiagnostics(build.Diagnostics, standardOut, errorOut);

                if (!build.Success || build.MapFile is null)
                {
                    return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
                }

                if (!transaction.TryPublish(build.MapFile, cancellationToken))
                {
                    return PreviewFailed(payloadRoot, diagnostics, standardOut, errorOut);
                }

                // Success printing observes no cancellation token: a commit that finished
                // must not be misreported by a late cancel.
                standardOut.WriteLine($"preview: {NormalizeWhitespace(transaction.TargetPath)}");

                if (transaction.RetainedBackupPath is not null)
                {
                    standardOut.WriteLine($"retained backup: {NormalizeWhitespace(transaction.RetainedBackupPath)}");
                }
            }

            return SuccessExit;
        }
        catch (OperationCanceledException)
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    $"cancelled: the preview was cancelled; the published payload at {payloadRoot} is unchanged"));
            return OperationalFailureExit;
        }
    }

    /// <summary>
    /// The package verb: converts a published payload into an H3 map package directory. The
    /// payload is an unmodified read input. An existing output directory is refused unless
    /// --force is set, because the package writer replaces it. Conversion is not cancellable,
    /// so cancellation only applies while the payload is opened.
    /// </summary>
    private static int RunPackage(PackageOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        var mapName = string.IsNullOrWhiteSpace(options.MapName)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.PayloadDirectory)))
            : options.MapName;

        if (string.IsNullOrWhiteSpace(mapName))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "package failed: the payload directory has no name to default the map name to; pass --name"));
            return OperationalFailureExit;
        }

        if (!options.Force && Path.Exists(options.OutputDirectory))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    $"package failed: the output {options.OutputDirectory} already exists; pass --force to replace it"));
            return OperationalFailureExit;
        }

        try
        {
            var diagnostics = new GeoDiagnosticBag();
            var reader = GeneratedMapPayloadReader.Open(options.PayloadDirectory, diagnostics, cancellationToken: cancellationToken);

            if (reader is null)
            {
                WriteDiagnostics(diagnostics.Items, standardOut, errorOut);
                errorOut.WriteLine(
                    NormalizeWhitespace(
                        $"package failed: the published payload at {options.PayloadDirectory} is unchanged"));
                return OperationalFailureExit;
            }

            using (reader)
            {
                var result = GeneratedMapPackageConverter.Convert(reader, mapName, options.OutputDirectory);

                if (!result.Success || result.PackageRoot is null)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        errorOut.WriteLine(NormalizeWhitespace(diagnostic));
                    }

                    errorOut.WriteLine(
                        NormalizeWhitespace(
                            $"package failed: the published payload at {options.PayloadDirectory} is unchanged"));
                    return OperationalFailureExit;
                }

                // Success printing observes no cancellation token: a package that finished
                // must not be misreported by a late cancel.
                standardOut.WriteLine($"package: {NormalizeWhitespace(result.PackageRoot)}");
                standardOut.WriteLine($"chunks: {result.ChunkCount}");

                foreach (var limitation in result.Limitations)
                {
                    standardOut.WriteLine($"limitation: {NormalizeWhitespace(limitation)}");
                }
            }

            return SuccessExit;
        }
        catch (OperationCanceledException)
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    $"cancelled: the package was cancelled; the published payload at {options.PayloadDirectory} is unchanged"));
            return OperationalFailureExit;
        }
    }

    private static int PreviewFailed(string payloadRoot, GeoDiagnosticBag diagnostics, TextWriter standardOut, TextWriter errorOut)
    {
        WriteDiagnostics(diagnostics.Items, standardOut, errorOut);
        errorOut.WriteLine(
            NormalizeWhitespace(
                $"preview failed: the published payload at {payloadRoot} is unchanged"));
        return OperationalFailureExit;
    }

    private static ITerrainTextureCatalog? TryResolveCatalog(GeoImportCliOptions? cliOptions, GeoDiagnosticBag diagnostics)
    {
        try
        {
            // Only the preview verb resolves an installation; import and report never
            // call this, so they never probe for one.
            return cliOptions?.TerrainTextureCatalogResolver is { } resolver
                ? resolver()
                : InstallationTerrainTextureCatalog.FromFirstInstalledGenerals();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoImportCliDiagnostics.CatalogUnavailable,
                GeoStage.Write,
                GeoSeverity.Error,
                $"The terrain texture catalog of the installed Generals could not be loaded: {ex.Message}"));
            return null;
        }
    }

    /// <summary>Streams the textures file in 64 KiB blocks under a 1 MiB encoded UTF-8 bound; never reads an oversized file whole.</summary>
    private static string? TryReadTextures(string path, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var contents = new MemoryStream();

            var block = new byte[TexturesBlockBytes];
            int read;

            while ((read = file.Read(block, 0, block.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (contents.Length + read > MaxTexturesBytes)
                {
                    diagnostics.Add(new GeoDiagnostic(
                        GeoImportCliDiagnostics.TexturesOversized,
                        GeoStage.Parse,
                        GeoSeverity.Error,
                        $"The textures file exceeds the {MaxTexturesBytes}-byte bound."));
                    return null;
                }

                contents.Write(block, 0, read);
            }

            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(contents.ToArray());
            }
            catch (DecoderFallbackException)
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoImportCliDiagnostics.TexturesNotUtf8,
                    GeoStage.Parse,
                    GeoSeverity.Error,
                    "The textures file is not valid UTF-8."));
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoImportCliDiagnostics.TexturesUnreadable,
                GeoStage.Parse,
                GeoSeverity.Error,
                $"The textures file could not be read: {ex.Message}",
                path));
            return null;
        }
    }

    private static bool TryParseCrop(string text, out PreviewCrop? crop)
    {
        crop = null;

        var parts = text.Split(',');

        if (parts.Length != 4)
        {
            return false;
        }

        var values = new int[4];

        for (var index = 0; index < 4; index++)
        {
            if (!int.TryParse(parts[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
        }

        crop = new PreviewCrop(values[0], values[1], values[2], values[3]);
        return true;
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
        standardOut.WriteLine($"masks: {report.Masks.ZeroCellMappedPolygonFeatureCount} zero-cell mapped polygon features");

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
                "usage: geoimport import --manifest <path> --out <dir> [--force]; "
                + "geoimport report --payload <dir>; "
                + "geoimport preview --payload <dir> --textures <map.json> [--crop cx0,cy0,cx1,cy1] [--force] --out <maps dir>; "
                + "geoimport package --payload <dir> --out <package dir> [--name <map name>] [--force]"));
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

    [Verb("preview", HelpText = "Converts a published payload into a playable legacy .map dev preview.")]
    public sealed class PreviewOptions
    {
        [Option('p', "payload", Required = true, HelpText = "Path to the published payload directory.")]
        public string PayloadDirectory { get; set; } = null!;

        [Option('t', "textures", Required = true, HelpText = "Path to the palette-to-terrain-texture JSON map.")]
        public string TexturesPath { get; set; } = null!;

        [Option('c', "crop", HelpText = "Half-open chunk rectangle, exactly four integers cx0,cy0,cx1,cy1.")]
        public string? Crop { get; set; }

        [Option('f', "force", HelpText = "Replace an existing .map, always retaining a backup of it.")]
        public bool Force { get; set; }

        [Option('o', "out", Required = true, HelpText = "Maps directory that receives <payload name>.map.")]
        public string MapsDirectory { get; set; } = null!;
    }

    [Verb("package", HelpText = "Converts a published payload into an H3 map package directory.")]
    public sealed class PackageOptions
    {
        [Option('p', "payload", Required = true, HelpText = "Path to the published payload directory.")]
        public string PayloadDirectory { get; set; } = null!;

        [Option('o', "out", Required = true, HelpText = "Output H3 map package directory.")]
        public string OutputDirectory { get; set; } = null!;

        [Option('n', "name", HelpText = "Map name; defaults to the payload directory name.")]
        public string? MapName { get; set; }

        [Option('f', "force", HelpText = "Replace an existing output directory.")]
        public bool Force { get; set; }
    }
}
