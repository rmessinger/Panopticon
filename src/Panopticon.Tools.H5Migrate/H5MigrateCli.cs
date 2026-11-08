#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// In-process entry of the H5Migrate CLI (WI-H5-01):
/// `convert-map --input &lt;legacy .map path&gt; --out &lt;package dir&gt;`.
/// Exit codes: 0 success, 1 operational or input failure, 2 usage error. The pipeline parses
/// the legacy map exactly like the corpus tests (MapFile.Decompress, then MapFile.FromStream),
/// reads it through the legacy reader, writes the H3 package through the staged atomic writer,
/// and verifies the package before reporting success: the manifest accounts for every source
/// section, typed sections compare semantically equal, and every retained section matches by
/// SHA-256. A failed conversion reports named diagnostics, exits non-zero, and leaves no
/// package at the output. Every diagnostic prints as one line `severity code convert-map
/// message` with whitespace normalized. Cancellation is observed before the package is
/// written, so a cancelled run never leaves a package behind.
/// </summary>
public static class H5MigrateCli
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
            .ParseArguments<ConvertMapOptions>(arguments)
            .MapResult(
                (ConvertMapOptions convertMapOptions) => RunConvertMap(convertMapOptions, standardOut, errorOut, cancellationToken),
                _ => UsageFailure(errorOut));
    }

    private static int RunConvertMap(ConvertMapOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        if (!Path.IsPathRooted(options.InputPath))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate convert-map --input <absolute legacy .map path> --out <package dir> (the input path must be absolute)"));
            return UsageExit;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var diagnostics = new List<MapPackageDiagnostic>();

            if (!File.Exists(options.InputPath))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.InputMissing,
                    $"The input map file '{options.InputPath}' does not exist."));
                return ConvertFailed(diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: false);
            }

            var sourceMap = TryParseSourceMap(options.InputPath, diagnostics);

            if (sourceMap is null)
            {
                return ConvertFailed(diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: false);
            }

            MapInterchange interchange;

            try
            {
                interchange = new LegacyMapReader(sourceMap, options.InputPath).Read();
            }
            catch (MapImportException exception)
            {
                diagnostics.AddRange(exception.Diagnostics.Select(d => new MapPackageDiagnostic(d.Code, d.Message)));
                return ConvertFailed(diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var writeResult = LegacyMapPackageImporter.Write(interchange, options.OutputDirectory);
            diagnostics.AddRange(writeResult.Diagnostics);

            if (!writeResult.Success)
            {
                return ConvertFailed(diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: false);
            }

            MapInterchange reloaded;

            try
            {
                reloaded = new PackageReader(options.OutputDirectory).Read();
            }
            catch (MapImportException exception)
            {
                diagnostics.Insert(0, new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.PackageReadFailed,
                    $"The written package failed to re-read: {Summarize(exception)}"));
                return ConvertFailed(diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: true);
            }

            var verification = MapPackageConversionVerifier.Verify(sourceMap, interchange, reloaded);

            if (!verification.Success)
            {
                return ConvertFailed(verification.Diagnostics, options.OutputDirectory, standardOut, errorOut, removeOutput: true);
            }

            // Success printing observes no cancellation token: a commit that finished
            // must not be misreported by a late cancel.
            standardOut.WriteLine(NormalizeWhitespace($"converted: {options.OutputDirectory}"));
            standardOut.WriteLine(
                NormalizeWhitespace(
                    $"source sections: {MapSourceSectionInventory.Describe(sourceMap).Count}, retained: {reloaded.RetainedSections.Count}"));
            return SuccessExit;
        }
        catch (OperationCanceledException)
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    $"cancelled: the conversion was cancelled before the package was written; {options.OutputDirectory} is unchanged"));
            return OperationalFailureExit;
        }
    }

    private static MapFile? TryParseSourceMap(string inputPath, List<MapPackageDiagnostic> diagnostics)
    {
        try
        {
            using var stream = File.OpenRead(inputPath);
            using var mapStream = MapFile.Decompress(stream);
            return MapFile.FromStream(mapStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                H5MigrateCliDiagnostics.InputUnreadable,
                $"The input map file '{inputPath}' could not be read: {ex.Message}"));
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException or EndOfStreamException or ArgumentException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                H5MigrateCliDiagnostics.InputUnparseable,
                $"The input map file '{inputPath}' is not a parseable legacy map ({ex.GetType().Name}: {ex.Message})."));
            return null;
        }
    }

    private static int ConvertFailed(
        IReadOnlyList<MapPackageDiagnostic> diagnostics,
        string outputDirectory,
        TextWriter standardOut,
        TextWriter errorOut,
        bool removeOutput)
    {
        if (removeOutput && Directory.Exists(outputDirectory))
        {
            try
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorOut.WriteLine(FormatDiagnostic(new MapPackageDiagnostic(
                    H5MigrateCliDiagnostics.OutputCleanupFailed,
                    $"Removing the failed package at '{outputDirectory}' failed: {ex.Message}")));
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            errorOut.WriteLine(FormatDiagnostic(diagnostic));
        }

        errorOut.WriteLine(
            NormalizeWhitespace(
                $"convert-map failed: no package was left at {outputDirectory}"));
        return OperationalFailureExit;
    }

    private static string Summarize(MapImportException exception) =>
        NormalizeWhitespace(string.Join("; ", exception.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    /// <summary>One-line diagnostic: `error code convert-map message` with whitespace normalized.</summary>
    internal static string FormatDiagnostic(MapPackageDiagnostic diagnostic) =>
        $"error {diagnostic.Code} convert-map {NormalizeWhitespace(diagnostic.Message)}";

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
                "usage: h5migrate convert-map --input <absolute legacy .map path> --out <package dir>"));
        return UsageExit;
    }

    [Verb("convert-map", HelpText = "Converts one legacy .map file into a verified H3 map package.")]
    public sealed class ConvertMapOptions
    {
        [Option('i', "input", Required = true, HelpText = "Absolute path to the legacy .map file.")]
        public string InputPath { get; set; } = null!;

        [Option('o', "out", Required = true, HelpText = "Output package directory.")]
        public string OutputDirectory { get; set; } = null!;
    }
}
