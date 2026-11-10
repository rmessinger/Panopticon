#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Map;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Round-trip validation for `validate` (WI-H5-03). Map mode compares a converted H3 package with its
/// legacy source; units mode compares a converted H4 package with the resolution of its roster.
/// Typed comparison and retained-byte checks delegate to the convert-time verifiers so each check has
/// one implementation; this type adds raw package checks, failure classification, and the report.
/// </summary>
public static class RoundTripValidator
{
    private const string MapMode = "map";

    private const string UnitsMode = "units";

    private static readonly string[] MapSections =
    [
        "sections", "identity", "layout", "heights", "blend", "objects", "waypoints", "layers", "retained",
    ];

    private static readonly string[] UnitSections = ["closure", "records", "retained", "diagnostics", "templates"];

    public static RoundTripReport ValidateMap(MapFile sourceMap, string legacyPath, string packageDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var findings = new List<RoundTripFinding>();
        AddRawMapRetainedFindings(packageDirectory, findings);

        MapInterchange expected;

        try
        {
            expected = new LegacyMapReader(sourceMap, legacyPath).Read();
        }
        catch (MapImportException exception)
        {
            findings.AddRange(exception.Diagnostics.Select(d =>
                RoundTripFailureClassifier.Operational(d.Code, "sections", d.Message)));
            return MapReport(findings);
        }

        MapInterchange reloaded;

        try
        {
            reloaded = new PackageReader(packageDirectory).Read();
        }
        catch (MapImportException exception)
        {
            findings.Add(RoundTripFailureClassifier.Operational(
                H5MigrateCliDiagnostics.PackageReadFailed,
                "retained",
                $"The package failed to read: {string.Join("; ", exception.Diagnostics.Select(d => $"{d.Code} {d.Message}"))}"));
            return MapReport(findings);
        }

        var verification = MapPackageConversionVerifier.Verify(sourceMap, expected, reloaded);
        findings.AddRange(verification.Diagnostics.Select(RoundTripFailureClassifier.ForMap));

        return MapReport(findings);
    }

    public static RoundTripReport ValidateUnits(
        UnitImportResolution expectedResolution,
        ILegacyNativeTable? expectedNatives,
        IUnitPackageRetainedPayloadProvider provider,
        string packageDirectory)
    {
        ArgumentNullException.ThrowIfNull(expectedResolution);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var findings = new List<RoundTripFinding>();
        var rootNames = expectedResolution.Roots.Select(root => root.Name).ToList();

        UnitContentSet reloadedSet;
        H4UnitPackageReader reader;

        try
        {
            reader = new H4UnitPackageReader(packageDirectory);
            reloadedSet = reader.Read(rootNames);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            findings.Add(RoundTripFailureClassifier.Operational(
                H5MigrateCliDiagnostics.PackageReadFailed,
                "records",
                $"The package failed to read: {exception.Message}"));
            return UnitsReport(expectedResolution, findings);
        }

        findings.AddRange(reloadedSet.Diagnostics
            .Where(d => d.IsError)
            .Select(d => RoundTripFailureClassifier.Operational(d.Code, "records", d.Message)));

        var reloadedResolution = UnitImportResolution.Resolve(reloadedSet, GeneralsUnitAdapter.CreateRegistry());

        findings.AddRange(reloadedResolution.Diagnostics
            .Where(d => d.IsError)
            .Select(d => RoundTripFailureClassifier.Operational(d.Code, "records", d.Message)));

        var verification = UnitPackageConversionVerifier.Verify(
            expectedResolution,
            reloadedSet,
            reloadedResolution,
            reader.Natives,
            provider,
            packageDirectory,
            allowProvenanceAllowlist: true);

        findings.AddRange(verification.Diagnostics.Select(RoundTripFailureClassifier.ForUnit));
        findings.AddRange(DiagnosticSetFindings(expectedResolution.Diagnostics, reloadedResolution.Diagnostics));

        if (expectedResolution.Succeeded && reloadedResolution.Succeeded)
        {
            findings.AddRange(RoundTripTemplateComparer.Compare(expectedResolution, expectedNatives, reloadedResolution, reader.Natives));
        }

        return UnitsReport(expectedResolution, findings);
    }

    private static RoundTripReport MapReport(List<RoundTripFinding> findings)
    {
        return RoundTripReport.Create(MapMode, MapSections, findings, []);
    }

    private static RoundTripReport UnitsReport(UnitImportResolution expected, List<RoundTripFinding> findings)
    {
        var records = expected.Closure
            .Select(record =>
            {
                var name = record.Id.Name;
                return new RoundTripRecordVerdict(name, findings.All(f => f.Subject != name));
            })
            .ToList();

        return RoundTripReport.Create(UnitsMode, UnitSections, findings, records);
    }

    private static void AddRawMapRetainedFindings(string packageDirectory, List<RoundTripFinding> findings)
    {
        var manifestPath = Path.Combine(packageDirectory, MapPackageFormat.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            findings.Add(RoundTripFailureClassifier.Operational(
                H5MigrateCliDiagnostics.PackageReadFailed,
                "retained",
                $"The package manifest is missing at '{manifestPath}'."));
            return;
        }

        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));

        if (manifest is null)
        {
            return;
        }

        foreach (var entry in manifest.RetainedSections)
        {
            var fullPath = Path.Combine(packageDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                findings.Add(new RoundTripFinding(
                    RoundTripFailureClassifier.CodeFor(RoundTripFailureClass.MissingSection),
                    RoundTripFailureClass.MissingSection,
                    "retained",
                    entry.SectionName,
                    $"Retained section '{entry.SectionName}' file is missing at '{entry.Path}'."));
                continue;
            }

            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();

            if (!string.Equals(actual, entry.Sha256, StringComparison.Ordinal))
            {
                findings.Add(new RoundTripFinding(
                    RoundTripFailureClassifier.CodeFor(RoundTripFailureClass.RetainedHashMismatch),
                    RoundTripFailureClass.RetainedHashMismatch,
                    "retained",
                    entry.SectionName,
                    $"Retained section '{entry.SectionName}' SHA-256 mismatch: manifest {entry.Sha256}, file {actual}."));
            }
        }
    }

    private static IEnumerable<RoundTripFinding> DiagnosticSetFindings(
        IEnumerable<ImportDiagnostic> expected,
        IEnumerable<ImportDiagnostic> actual)
    {
        var expectedCounts = expected.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var actualCounts = actual.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var code in expectedCounts.Keys.Union(actualCounts.Keys, StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal))
        {
            expectedCounts.TryGetValue(code, out var expectedCount);
            actualCounts.TryGetValue(code, out var actualCount);

            if (expectedCount != actualCount)
            {
                yield return new RoundTripFinding(
                    RoundTripFailureClassifier.CodeFor(RoundTripFailureClass.TypedFieldMismatch),
                    RoundTripFailureClass.TypedFieldMismatch,
                    "diagnostics",
                    code,
                    $"Diagnostic '{code}' count differs: expected {expectedCount}, got {actualCount}.");
            }
        }
    }
}
