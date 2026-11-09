#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.IO;
using Panopticon.Tests.Content.Importing.Units.Packages;
using Panopticon.Tests.InstalledAssets;
using Panopticon.Tools.H5Migrate;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Installed-assets variants of the WI-H7-02 unit round-trip suite. Generals converts the P6b
/// archetype roster (USA reference units) through the actual convert-ini CLI and validates each
/// package through the shared H5-03 RoundTripValidator. EE2 resolves a DDF-sourced unit through
/// the H2a EE2 adapter and verifies the converted package's record-level round trip through the
/// validator's own record comparer (UnitPackageConversionVerifier); see the EE2 test comment for
/// the depth statement. Missing installs skip with a recorded reason; strict mode
/// (PANOPTICON_STRICT_INSTALLED) makes missing or vacuous execution a failure. Executed counts are
/// reported separately from skips.
/// </summary>
public sealed class H7UnitRoundTripInstalledTests
{
    private readonly ITestOutputHelper _output;

    public H7UnitRoundTripInstalledTests(ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    [InstalledAssetsFact(SageGame.CncGenerals)]
    [Trait("Category", InstalledAssetsEnvironment.CategoryName)]
    public void InstalledGeneralsArchetypeRoster_RoundTrip_ValidatesNonVacuously()
    {
        var installation = InstalledAssetsEnvironment.RequireInstallation(SageGame.CncGenerals);
        var installPath = installation.Path;
        var executedCount = 0;
        var skippedCount = 0;
        var validatedUnits = new List<string>();
        var tempRoot = NewTempRoot();

        try
        {
            foreach (var unitName in UnitPackageRoster.ArchetypeUnits)
            {
                var packageDir = Path.Combine(tempRoot, unitName);

                using (var convertOut = new StringWriter())
                using (var convertErr = new StringWriter())
                {
                    var convertExit = H5MigrateCli.Run(
                        ["convert-ini", "--game-path", installPath, "--out", packageDir, unitName],
                        convertOut,
                        convertErr,
                        CancellationToken.None);

                    if (convertExit != 0)
                    {
                        skippedCount++;
                        _output.WriteLine($"Conversion skipped for '{unitName}' (exit {convertExit}): {convertErr}");
                        continue;
                    }
                }

                // The retained-INI provider must see the same composite view the pipeline converts
                // from: loose files plus the .big archives that carry the installed INI corpus.
                using var installFileSystem = new CompositeFileSystem(new DiskFileSystem(installPath), new BigFileSystem(installPath));
                var expected = LoadExpectedResolution(installFileSystem, [unitName]);
                Assert.True(expected.Resolution.Closure.Any(record => record.Id.Name == unitName), $"Closure is missing roster unit '{unitName}'.");
                Assert.True(expected.Resolution.Closure.Count >= 2, $"Closure for '{unitName}' is suspiciously small ({expected.Resolution.Closure.Count} records).");

                var provider = new IniRetainedPayloadProvider(installFileSystem);
                var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);
                Assert.True(
                    report.Passed,
                    $"Round-trip validation failed for '{unitName}': {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");

                Assert.Contains(report.Records, record => record.Record == unitName && record.Passed);

                executedCount++;
                validatedUnits.Add(unitName);
            }

            // Non-vacuous guard: every P6b archetype must convert and validate on the real install;
            // a skip is recorded with its reason but fails the proof.
            Assert.True(
                executedCount == UnitPackageRoster.ArchetypeUnits.Count,
                $"Vacuous execution guard failed: expected all {UnitPackageRoster.ArchetypeUnits.Count} archetypes to round-trip, got {executedCount} executed, {skippedCount} skipped.");

            InstalledAssetsEnvironment.RecordCoverage(
                _output,
                "H7-02 Generals P6b archetype round-trip",
                executedCount,
                skippedCount,
                $"Units: {string.Join(", ", validatedUnits)}");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// EE2 depth shipped (stated per the card contract): non-vacuous DDF resolution plus the
    /// converter's record-level round trip. The DDF-sourced maceman closure is resolved through the
    /// H2a EE2 adapter with the named exclusion policy, written by H4UnitPackageWriter (EE2 records
    /// are not legacy-INI-bound, so no retained blocks are needed), re-read through
    /// H4UnitPackageReader, re-resolved, and compared through the shared H5 verifier
    /// UnitPackageConversionVerifier. The full RoundTripValidator.ValidateUnits report is not
    /// asserted for EE2 because its materialized-template comparison runs on a Generals conversion
    /// policy (no declared game-units-per-source-distance scale for EE2 distances) and its
    /// diagnostic-set gate counts resolution-stage warnings against package read-stage diagnostics;
    /// extending the validator for EE2 is production work outside this card, and the limitation is
    /// not pinned here as expected behavior.
    /// </summary>
    [InstalledAssetsEe2Fact]
    [Trait("Category", InstalledAssetsEnvironment.CategoryName)]
    public void InstalledEe2DdfUnit_ResolutionAndRecordRoundTrip_ValidatesNonVacuously()
    {
        var installRoot = InstalledAssetsEe2Environment.RequireInstall();

        var sourceFiles = new Ee2SourceFiles(
            new ZipFileSystem(Path.Combine(installRoot, "zips/db.zip")),
            new ZipFileSystem(Path.Combine(installRoot, "zips/graphics.zip")),
            ["db/Units/human.ddf", "db/Units/heavy_infantry1.ddf"],
            Ee2SourceFiles.DefaultUnitTablePath);

        var adapter = new Ee2UnitAdapter(sourceFiles, new Ee2ImportPolicy(1, "we", Ee2RealUnitImportTests.Exclusions));
        var set = adapter.Read(["HeavyInfantry1"]);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());

        Assert.True(
            resolution.Succeeded,
            string.Join("; ", resolution.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));

        // Non-vacuous guards: the maceman closure carries unit, locomotor, weapon, visual, and
        // attack-ability records with authored values.
        Assert.True(resolution.Closure.Count >= 5, $"EE2 closure is suspiciously small ({resolution.Closure.Count} records).");
        var unitId = new ContentId(ContentSource.Ee2, ContentKind.Unit, "HeavyInfantry1UpgradeEpoch1");
        var unit = resolution.Get<UnitRecord>(unitId);
        Assert.Equal(75, unit.MaxHealth.Value.Value);
        Assert.Equal(5, unit.VisionRange.Value.Value);
        Assert.Equal(37, unit.BuildTime.Value.Value);
        Assert.True(unit.Locomotors.Resolved.Count > 0);
        Assert.True(unit.Visual.IsPresent);

        // Lossy-converter diagnostics surface named, never silent: every excluded gameplay mechanic
        // carries the policy-exclusion warning naming the field, and non-gameplay fields warn.
        var excludedCodes = resolution.Diagnostics
            .Where(diagnostic => diagnostic.Code == DiagnosticCodes.ExcludedByPolicy)
            .Select(diagnostic => diagnostic.Message)
            .ToList();
        Assert.Contains(excludedCodes, message => message.Contains("ownertype", StringComparison.Ordinal));
        Assert.Contains(excludedCodes, message => message.Contains("rps", StringComparison.Ordinal));
        Assert.Contains(
            resolution.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCodes.NonGameplayMetadata);

        // The source records keep their named unclassified fields with gameplay and policy flags.
        Assert.Contains(unit.Unclassified, field => field.Name == "ownertype" && field.AffectsGameplay && field.ExcludedByPolicy);

        // Record-level round trip through the converter: write (no retained provider needed), read,
        // re-resolve, and compare through the shared verifier the validator itself delegates to.
        var tempRoot = NewTempRoot();
        var packageDir = Path.Combine(tempRoot, "ee2-package");

        try
        {
            var write = H4UnitPackageWriter.Write(packageDir, resolution);
            Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

            var reader = new H4UnitPackageReader(packageDir);
            var reloadedSet = reader.Read(resolution.Roots.Select(root => root.Name).ToList());
            Assert.DoesNotContain(reloadedSet.Diagnostics, diagnostic => diagnostic.IsError);

            var reloadedResolution = UnitImportResolution.Resolve(reloadedSet, GeneralsUnitAdapter.CreateRegistry());
            Assert.True(
                reloadedResolution.Succeeded,
                string.Join("; ", reloadedResolution.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));

            var verification = UnitPackageConversionVerifier.Verify(
                resolution,
                reloadedSet,
                reloadedResolution,
                reader.Natives,
                new EmptyEe2RetainedPayloadProvider(),
                packageDir,
                allowProvenanceAllowlist: true);

            Assert.True(
                verification.Success,
                string.Join("; ", verification.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

            var reloadedUnit = reloadedResolution.Get<UnitRecord>(unitId);
            Assert.Equal(75, reloadedUnit.MaxHealth.Value.Value);
            Assert.Contains(reloadedUnit.Unclassified, field => field.Name == "ownertype" && field.AffectsGameplay && field.ExcludedByPolicy);

            InstalledAssetsEnvironment.RecordCoverage(
                _output,
                "H7-02 EE2 DDF resolution and record round-trip",
                1,
                0,
                $"Unit: HeavyInfantry1 closure={resolution.Closure.Count} records");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static LoadedRoster LoadExpectedResolution(FileSystem installFileSystem, IReadOnlyList<string> rosterRoots)
    {
        var staging = GeneralsIniLoader.Load(installFileSystem);
        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);

        // Mirror H5MigrateCli.ResolveRosterUnits: the pipeline prepends DefaultThingTemplate because
        // Generals objects inherit its default modules.
        var roots = new List<string> { "DefaultThingTemplate" };
        roots.AddRange(rosterRoots);
        var set = adapter.Read(roots);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(
            resolution.Succeeded,
            string.Join("; ", resolution.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));
        return new LoadedRoster(adapter, resolution);
    }

    private sealed record LoadedRoster(GeneralsUnitAdapter Adapter, UnitImportResolution Resolution);

    /// <summary>EE2 records carry no legacy bindings; any retention request is a suite bug.</summary>
    private sealed class EmptyEe2RetainedPayloadProvider : IUnitPackageRetainedPayloadProvider
    {
        public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
        {
            throw new InvalidOperationException($"No EE2 record needs retention, but '{request.RecordId}' requested a block.");
        }
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonH7Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }
}
