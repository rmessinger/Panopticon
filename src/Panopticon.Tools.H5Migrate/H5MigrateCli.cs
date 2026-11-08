#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CommandLine;
using Panopticon.Content;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.Data.Map;
using Panopticon.IO;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// In-process entry of the H5Migrate CLI (WI-H5-01, WI-H5-02):
/// `convert-map --input &lt;legacy .map path&gt; --out &lt;package dir&gt;`,
/// `convert-ini --out &lt;package dir&gt; [--game-path &lt;path&gt;] [--p6b] [&lt;unit name&gt; ...]`,
/// `validate --package &lt;package dir&gt; (--input &lt;legacy .map&gt; | [--game-path &lt;path&gt;] [--p6b] [&lt;unit name&gt; ...]) [--json]`.
/// Exit codes: 0 success or identical content, 1 operational failure or mismatch, 2 usage error. The pipeline parses
/// legacy content, writes staged packages atomically, verifies the result before reporting success,
/// and deletes the output on failure so no partial packages remain.
/// </summary>
public static class H5MigrateCli
{
    private const int SuccessExit = 0;

    private const int OperationalFailureExit = 1;

    private const int UsageExit = 2;

    private static readonly IReadOnlyList<string> P6bArchetypeUnits =
    [
        "AmericaInfantryRanger",
        "AmericaTankCrusader",
        "AmericaVehicleComanche",
        "AmericaVehicleDozer",
        "AmericaVehicleMedic",
        "AmericaParticleCannonUplink",
    ];

    public static int Run(string[] arguments, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOut);
        ArgumentNullException.ThrowIfNull(errorOut);

        // HelpWriter null keeps the parser silent; usage handling stays here so the
        // streams under test carry exactly the intended lines.
        var parser = new Parser(with => with.HelpWriter = null);

        return parser
            .ParseArguments<ConvertMapOptions, ConvertIniOptions, ValidateOptions>(arguments)
            .MapResult(
                (ConvertMapOptions convertMapOptions) => RunConvertMap(convertMapOptions, standardOut, errorOut, cancellationToken),
                (ConvertIniOptions convertIniOptions) => RunConvertIni(convertIniOptions, standardOut, errorOut, cancellationToken),
                (ValidateOptions validateOptions) => RunValidate(validateOptions, standardOut, errorOut, cancellationToken),
                _ => UsageFailure(arguments, errorOut));
    }

    private static int RunValidate(ValidateOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        var mapMode = options.InputPath is not null;
        var unitsMode = HasRosterSpecification(options.P6b, options.Units, options.PositionalUnits);

        if (string.IsNullOrWhiteSpace(options.PackageDirectory) || !Path.IsPathRooted(options.PackageDirectory) || mapMode == unitsMode)
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate validate --input <absolute legacy .map path> --package <package dir> [--json] | validate --package <package dir> [--game-path <path>] [--p6b] [<unit name> ...] [--json] (the package path must be absolute; give either --input or a roster)"));
            return UsageExit;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            RoundTripReport report;

            if (mapMode)
            {
                if (!Path.IsPathRooted(options.InputPath))
                {
                    errorOut.WriteLine(
                        NormalizeWhitespace(
                            "usage: h5migrate validate --input <absolute legacy .map path> --package <package dir> (the input path must be absolute)"));
                    return UsageExit;
                }

                if (!File.Exists(options.InputPath))
                {
                    errorOut.WriteLine(FormatDiagnostic("error", H5MigrateCliDiagnostics.InputMissing, "validate",
                        $"The input map file '{options.InputPath}' does not exist."));
                    return OperationalFailureExit;
                }

                var parseDiagnostics = new List<MapPackageDiagnostic>();
                var sourceMap = TryParseSourceMap(options.InputPath, parseDiagnostics);

                if (sourceMap is null)
                {
                    foreach (var diag in parseDiagnostics)
                    {
                        errorOut.WriteLine(FormatDiagnostic("error", diag.Code, "validate", diag.Message));
                    }

                    return OperationalFailureExit;
                }

                report = RoundTripValidator.ValidateMap(sourceMap, options.InputPath, options.PackageDirectory);
            }
            else
            {
                var gamePath = options.GamePath ?? options.InstallPath ?? Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
                if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                {
                    errorOut.WriteLine(
                        FormatDiagnostic("error", H5MigrateCliDiagnostics.InstallMissing, "validate",
                            $"Generals installation not found: '{gamePath ?? "(not set)"}'. Set CNC_GENERALS_PATH or pass --game-path."));
                    return OperationalFailureExit;
                }

                using var fileSystem = new CompositeFileSystem(
                    new DiskFileSystem(gamePath),
                    new BigFileSystem(gamePath));

                if (fileSystem.GetFile(@"Data\INI\Armor.ini") is null)
                {
                    errorOut.WriteLine(
                        FormatDiagnostic("error", H5MigrateCliDiagnostics.InstallMissing, "validate",
                            $"Generals installation at '{gamePath}' does not contain expected data files."));
                    return OperationalFailureExit;
                }

                var staging = GeneralsIniLoader.Load(fileSystem);
                var roster = ResolveRosterUnits(options.P6b, options.Units, options.PositionalUnits);
                var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
                var set = adapter.Read(roster);
                var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());

                if (!resolution.Succeeded)
                {
                    foreach (var line in IniConversionRecordReport.Create(resolution).FormatLines("validate"))
                    {
                        errorOut.WriteLine(line);
                    }

                    return OperationalFailureExit;
                }

                cancellationToken.ThrowIfCancellationRequested();

                report = RoundTripValidator.ValidateUnits(
                    resolution,
                    adapter.Natives,
                    new IniRetainedPayloadProvider(fileSystem),
                    options.PackageDirectory);
            }

            if (options.Json)
            {
                standardOut.WriteLine(RoundTripReportWriter.ToJson(report));
            }
            else
            {
                foreach (var line in RoundTripReportWriter.ToText(report))
                {
                    standardOut.WriteLine(line);
                }
            }

            return report.Passed ? SuccessExit : OperationalFailureExit;
        }
        catch (OperationCanceledException)
        {
            errorOut.WriteLine(NormalizeWhitespace("cancelled: the validation was cancelled before a report was produced"));
            return OperationalFailureExit;
        }
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

    private static int RunConvertIni(ConvertIniOptions options, TextWriter standardOut, TextWriter errorOut, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OutputDirectory) || !Path.IsPathRooted(options.OutputDirectory))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate convert-ini --out <package dir> [--game-path <path>] [--p6b] [<unit name> ...] (the output path must be absolute)"));
            return UsageExit;
        }

        if (!HasRosterSpecification(options))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate convert-ini --out <package dir> [--game-path <path>] [--p6b] [<unit name> ...] (specify --p6b or at least one unit object name)"));
            return UsageExit;
        }

        var rosterUnits = ResolveRosterUnits(options);

        var gamePath = options.GamePath ?? options.InstallPath ?? Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            errorOut.WriteLine(
                FormatDiagnostic("error", H5MigrateCliDiagnostics.InstallMissing, "convert-ini",
                    $"Generals installation not found: '{gamePath ?? "(not set)"}'. Set CNC_GENERALS_PATH or pass --game-path."));
            errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
            return OperationalFailureExit;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var fileSystem = new CompositeFileSystem(
                new DiskFileSystem(gamePath),
                new BigFileSystem(gamePath));

            if (fileSystem.GetFile(@"Data\INI\Armor.ini") is null)
            {
                errorOut.WriteLine(
                    FormatDiagnostic("error", H5MigrateCliDiagnostics.InstallMissing, "convert-ini",
                        $"Generals installation at '{gamePath}' does not contain expected data files."));
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            var staging = GeneralsIniLoader.Load(fileSystem);

            cancellationToken.ThrowIfCancellationRequested();

            var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);
            var set = adapter.Read(rosterUnits);
            var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());

            if (!resolution.Succeeded)
            {
                var report = IniConversionRecordReport.Create(resolution);
                foreach (var line in report.FormatLines("convert-ini"))
                {
                    errorOut.WriteLine(line);
                }

                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            var provider = new IniRetainedPayloadProvider(fileSystem);

            // Pre-write gate: verify every legacy-bound record needing retention has a block from the provider
            var missingRetained = false;
            foreach (var record in resolution.Closure)
            {
                if (UnitPackageConversionVerifier.RecordNeedsRetention(record))
                {
                    var block = provider.GetBlock(new UnitRetainedBlockRequest(record.Id));
                    if (block is null || block.Bytes is null || block.Bytes.Length == 0)
                    {
                        missingRetained = true;
                        errorOut.WriteLine(FormatDiagnostic("error", H5MigrateCliDiagnostics.RetainedBlockMissing, "convert-ini",
                            $"Retained INI block could not be sourced for record '{record.Id}'."));
                    }
                }
            }

            if (missingRetained)
            {
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var writeResult = H4UnitPackageWriter.Write(options.OutputDirectory, resolution, provider);
            if (!writeResult.Success)
            {
                foreach (var diag in writeResult.Diagnostics)
                {
                    errorOut.WriteLine(FormatDiagnostic("error", diag.Code, "convert-ini", diag.Message));
                }

                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            // Post-write verification
            H4UnitPackageReader reader;
            UnitContentSet reloadedSet;
            try
            {
                reader = new H4UnitPackageReader(options.OutputDirectory);
                reloadedSet = reader.Read(resolution.Roots.Select(r => r.Name).ToList());
            }
            catch (Exception ex)
            {
                errorOut.WriteLine(FormatDiagnostic("error", H5MigrateCliDiagnostics.PackageReadFailed, "convert-ini",
                    $"The written package failed to re-read: {ex.Message}"));
                CleanupDirectory(options.OutputDirectory, errorOut);
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            if (reloadedSet.Diagnostics.Any(d => d.IsError))
            {
                foreach (var err in reloadedSet.Diagnostics.Where(d => d.IsError))
                {
                    errorOut.WriteLine(FormatDiagnostic("error", err.Code, "convert-ini", err.Message));
                }

                CleanupDirectory(options.OutputDirectory, errorOut);
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            var reloadedResolution = UnitImportResolution.Resolve(reloadedSet, GeneralsUnitAdapter.CreateRegistry());
            if (!reloadedResolution.Succeeded)
            {
                foreach (var err in reloadedResolution.Diagnostics.Where(d => d.IsError))
                {
                    errorOut.WriteLine(FormatDiagnostic("error", err.Code, "convert-ini", err.Message));
                }

                CleanupDirectory(options.OutputDirectory, errorOut);
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            var verification = UnitPackageConversionVerifier.Verify(
                resolution,
                reloadedSet,
                reloadedResolution,
                reader.Natives,
                provider,
                options.OutputDirectory);

            if (!verification.Success)
            {
                foreach (var diag in verification.Diagnostics)
                {
                    errorOut.WriteLine(FormatDiagnostic("error", diag.Code, "convert-ini", diag.Message));
                }

                CleanupDirectory(options.OutputDirectory, errorOut);
                errorOut.WriteLine(NormalizeWhitespace($"convert-ini failed: no package was left at {options.OutputDirectory}"));
                return OperationalFailureExit;
            }

            var retainedCount = resolution.Closure.Count(UnitPackageConversionVerifier.RecordNeedsRetention);
            standardOut.WriteLine(NormalizeWhitespace($"converted: {options.OutputDirectory}"));
            standardOut.WriteLine(NormalizeWhitespace($"records: {resolution.Closure.Count}, retained: {retainedCount}"));
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

    private static bool HasRosterSpecification(ConvertIniOptions options)
    {
        return HasRosterSpecification(options.P6b, options.Units, options.PositionalUnits);
    }

    private static bool HasRosterSpecification(bool p6b, IEnumerable<string>? units, IEnumerable<string>? positionalUnits)
    {
        return p6b
            || (units is not null && units.Any(u => !string.IsNullOrWhiteSpace(u)))
            || (positionalUnits is not null && positionalUnits.Any(u => !string.IsNullOrWhiteSpace(u)));
    }

    private static IReadOnlyList<string> ResolveRosterUnits(ConvertIniOptions options)
    {
        return ResolveRosterUnits(options.P6b, options.Units, options.PositionalUnits);
    }

    private static IReadOnlyList<string> ResolveRosterUnits(bool p6b, IEnumerable<string>? units, IEnumerable<string>? positionalUnits)
    {
        var resolved = new List<string>
        {
            // Generals objects inherit default modules from DefaultThingTemplate.
            "DefaultThingTemplate",
        };

        if (p6b)
        {
            resolved.AddRange(P6bArchetypeUnits);
        }

        if (units is not null)
        {
            resolved.AddRange(units.Where(u => !string.IsNullOrWhiteSpace(u)));
        }

        if (positionalUnits is not null)
        {
            resolved.AddRange(positionalUnits.Where(u => !string.IsNullOrWhiteSpace(u)));
        }

        return resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CleanupDirectory(string directory, TextWriter errorOut)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorOut.WriteLine(FormatDiagnostic(
                    "error",
                    H5MigrateCliDiagnostics.OutputCleanupFailed,
                    "convert-ini",
                    $"Removing the failed package at '{directory}' failed: {ex.Message}"));
            }
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

    /// <summary>One-line diagnostic: `severity code subcommand message` with whitespace normalized.</summary>
    internal static string FormatDiagnostic(string severity, string code, string subcommand, string message) =>
        $"{severity} {code} {subcommand} {NormalizeWhitespace(message)}";

    /// <summary>One-line diagnostic: `error code convert-map message` with whitespace normalized.</summary>
    internal static string FormatDiagnostic(MapPackageDiagnostic diagnostic) =>
        FormatDiagnostic("error", diagnostic.Code, "convert-map", diagnostic.Message);

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

    private static int UsageFailure(string[] arguments, TextWriter errorOut)
    {
        if (arguments.Length > 0 && string.Equals(arguments[0], "convert-ini", StringComparison.OrdinalIgnoreCase))
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate convert-ini --out <package dir> [--game-path <path>] [--p6b] [<unit name> ...]"));
        }
        else
        {
            errorOut.WriteLine(
                NormalizeWhitespace(
                    "usage: h5migrate convert-map --input <absolute legacy .map path> --out <package dir>"));
        }

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
