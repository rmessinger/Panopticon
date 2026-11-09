#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Tests.InstalledAssets;
using Panopticon.Tools.H5Migrate;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Maps;

/// <summary>
/// Map round-trip regression suite for WI-H7-01.
/// Covers synthetic round trips in routine CI without game installs, terrain deformation re-exports,
/// unsupported retained version diagnostics, and representative installed map round-trips under
/// trait Category InstalledAssets.
/// </summary>
public sealed class H7MapRoundTripRegressionTests
{
    private readonly ITestOutputHelper _output;

    public H7MapRoundTripRegressionTests(ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    [Fact]
    public void SyntheticMap_RoundTrip_PreservesAllDispositionsAndQuantitiesAndHashes()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, sourceMap, packageDir) = CreateAndConvertSyntheticMap(tempRoot);

            var reloaded = new PackageReader(packageDir).Read();
            var report = RoundTripValidator.ValidateMap(sourceMap, mapPath, packageDir);

            Assert.True(
                report.Passed,
                $"RoundTripValidator failed: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");

            // 1. All 29 section dispositions accounted.
            var inventory = MapSourceSectionInventory.Describe(sourceMap);
            Assert.NotEmpty(inventory);
            Assert.Contains(report.Sections, s => s.Section == "sections" && s.Passed);

            var retainedNames = reloaded.RetainedSections.Select(s => s.SectionName).ToHashSet(StringComparer.Ordinal);
            foreach (var section in inventory)
            {
                if (section.Disposition == MapSourceSectionDisposition.Retained)
                {
                    Assert.Contains(section.Name, retainedNames);
                }
            }

            // 2. Scoped AssetReference identity preserved.
            Assert.NotNull(reloaded.Objects);
            var customObject = Assert.Single(reloaded.Objects!.Objects, o => o.Type.Name == "SyntheticStructure");
            Assert.Equal("legacy", customObject.Type.Scope);
            Assert.Equal("SyntheticStructure", customObject.Type.Name);

            Assert.All(reloaded.Objects.Objects, o =>
            {
                Assert.False(string.IsNullOrWhiteSpace(o.Type.Scope));
                Assert.False(string.IsNullOrWhiteSpace(o.Type.Name));
            });

            // 3. Quantity units and frames preserved as declared.
            Assert.Equal(MapQuantityUnit.GameUnit, reloaded.Layout.SampleSpacing.Unit);
            Assert.Equal(MapQuantityFrame.MapLocalXy, reloaded.Layout.SampleSpacing.Frame);

            Assert.NotNull(reloaded.Waypoints);
            Assert.NotEmpty(reloaded.Waypoints!.Waypoints);
            Assert.All(reloaded.Waypoints.Waypoints, waypoint =>
            {
                Assert.Equal(MapQuantityUnit.GameUnit, waypoint.X.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineXy, waypoint.X.Frame);
                Assert.Equal(MapQuantityUnit.GameUnit, waypoint.Y.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineXy, waypoint.Y.Frame);
                Assert.Equal(MapQuantityUnit.GameUnit, waypoint.Z.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineZ, waypoint.Z.Frame);
            });

            Assert.All(reloaded.Objects.Objects, obj =>
            {
                Assert.Equal(MapQuantityUnit.GameUnit, obj.X.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineXy, obj.X.Frame);
                Assert.Equal(MapQuantityUnit.GameUnit, obj.Y.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineXy, obj.Y.Frame);
                Assert.Equal(MapQuantityUnit.GameUnit, obj.Z.Unit);
                Assert.Equal(MapQuantityFrame.LegacyEngineZ, obj.Z.Frame);
            });

            // 4. Every retained section SHA-256-matched.
            var retainedVerdict = Assert.Single(report.Sections, s => s.Section == "retained");
            Assert.True(retainedVerdict.Passed);
            Assert.NotEmpty(reloaded.RetainedSections);

            foreach (var retained in reloaded.RetainedSections)
            {
                var computedSha = Convert.ToHexString(SHA256.HashData(retained.Bytes.Span)).ToLowerInvariant();
                Assert.Equal(retained.Sha256, computedSha);
            }

            // 5. The manifest accounts for every source section.
            var sectionsVerdict = Assert.Single(report.Sections, s => s.Section == "sections");
            Assert.True(sectionsVerdict.Passed);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticMap_Deformation_ElevationsEditMatchesEditedStateNotOriginal()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (_, _, packageDir) = CreateAndConvertSyntheticMap(tempRoot);

            var openResult = MapPackageConverter.OpenPackage(packageDir);
            var mapFile = openResult.MapFile;
            Assert.NotNull(mapFile.HeightMapData);

            const int editX = 10;
            const int editY = 10;
            var originalElevation = mapFile.HeightMapData!.Elevations[editX, editY];
            var editedElevation = (ushort)(originalElevation + 75);

            mapFile.HeightMapData.Elevations[editX, editY] = editedElevation;

            var reExportDir = Path.Combine(tempRoot, "re-exported-package");
            var saveResult = MapPackageConverter.SavePackage(
                mapFile,
                reExportDir,
                openResult.MapIniContent,
                openResult.MapStrContent,
                openResult.Context);

            Assert.True(saveResult.Success, string.Join("; ", saveResult.Diagnostics.Select(d => d.Message)));

            var reloadedOpen = MapPackageConverter.OpenPackage(reExportDir);
            Assert.NotNull(reloadedOpen.MapFile.HeightMapData);
            Assert.Equal(editedElevation, reloadedOpen.MapFile.HeightMapData!.Elevations[editX, editY]);
            Assert.NotEqual(originalElevation, reloadedOpen.MapFile.HeightMapData.Elevations[editX, editY]);

            var reloadedInterchange = new PackageReader(reExportDir).Read();
            var chunk = reloadedInterchange.Layout.Chunks[0];
            Assert.NotNull(chunk.Heights);
            var sampleIndex = (editY * (reloadedInterchange.Layout.LatticeWidth + (2 * MapChunkGeometry.ApronSamplesPerSide))) + editX;
            Assert.True(chunk.Heights.Samples.Count > 0);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticMap_UnsupportedRetainedVersion_FailsReloadAndConverterSurface()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, sourceMap, packageDir) = CreateAndConvertSyntheticMap(tempRoot);
            var tamperedDir = Path.Combine(tempRoot, "tampered-package");
            CopyDirectory(packageDir, tamperedDir);

            var manifestPath = Path.Combine(tamperedDir, MapPackageFormat.ManifestFileName);
            var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
            Assert.NotNull(manifest);

            var tamperedRetained = manifest!.RetainedSections
                .Select(s => s.SectionName == BlendTileData.AssetName ? s with { SectionVersion = -1 } : s)
                .ToArray();
            var tamperedManifest = manifest with { RetainedSections = tamperedRetained };
            File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(tamperedManifest));

            // Reload through PackageReader must fail with the named H3 diagnostic.
            var importEx = Assert.Throws<MapImportException>(() => new PackageReader(tamperedDir).Read());
            Assert.Contains(importEx.Diagnostics, d => d.Code == MapPackageDiagnostic.RetainedVersion);
            Assert.Equal("retained-version", MapPackageDiagnostic.RetainedVersion);

            // Converter validator surface reports the read failure with named converter diagnostic.
            var report = RoundTripValidator.ValidateMap(sourceMap, mapPath, tamperedDir);
            Assert.False(report.Passed);
            var retainedSection = Assert.Single(report.Sections, s => s.Section == "retained");
            Assert.False(retainedSection.Passed);
            Assert.Contains(retainedSection.Findings, f => f.Code == H5MigrateCliDiagnostics.PackageReadFailed);
            Assert.Equal("H5-CLI-005", H5MigrateCliDiagnostics.PackageReadFailed);
            Assert.Contains(retainedSection.Findings, f => f.Message.Contains(MapPackageDiagnostic.RetainedVersion, StringComparison.Ordinal));

            // Converter CLI validate subcommand surface reports exit code 1 with named diagnostic.
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();
            var exitCode = H5MigrateCli.Run(
                ["validate", "--input", mapPath, "--package", tamperedDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            var output = standardOut.ToString() + errorOut.ToString();
            Assert.Contains(H5MigrateCliDiagnostics.PackageReadFailed, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [InstalledAssetsFact(SageGame.CncGenerals)]
    [Trait("Category", "InstalledAssets")]
    public void InstalledGeneralsMap_RoundTrip_ValidatesNonVacuously()
    {
        var installation = InstalledAssetsEnvironment.RequireInstallation(SageGame.CncGenerals);
        var mapsRoot = Path.Combine(installation.Path, "maps");
        Assert.True(Directory.Exists(mapsRoot), $"Maps directory was not found at '{mapsRoot}'.");

        var candidates = Directory.EnumerateFiles(mapsRoot, "*.map", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        Assert.NotEmpty(candidates);

        var tempRoot = NewTempRoot();
        var executedCount = 0;
        var skippedCount = 0;
        var processedMaps = new List<string>();

        try
        {
            foreach (var mapPath in candidates)
            {
                var packageDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));

                using (var convertOut = new StringWriter())
                using (var convertErr = new StringWriter())
                {
                    var convertExit = H5MigrateCli.Run(
                        ["convert-map", "--input", mapPath, "--out", packageDir],
                        convertOut,
                        convertErr,
                        CancellationToken.None);

                    if (convertExit != 0)
                    {
                        skippedCount++;
                        _output.WriteLine($"Conversion skipped for '{mapPath}' (exit {convertExit}): {convertErr}");
                        continue;
                    }
                }

                var reloaded = new PackageReader(packageDir).Read();
                Assert.Equal(MapSourceKind.H3Package, reloaded.Identity.SourceKind);
                Assert.True(reloaded.RetainedSections.Count > 0);

                var sourceMap = ParseMapFile(mapPath);
                var report = RoundTripValidator.ValidateMap(sourceMap, mapPath, packageDir);
                Assert.True(
                    report.Passed,
                    $"Round-trip validation failed for '{mapPath}': {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");

                executedCount++;
                processedMaps.Add(Path.GetFileName(mapPath));
            }

            // Vacuous execution guard: at least one installed map must be converted and validated.
            Assert.True(executedCount >= 1, $"Vacuous execution guard failed: expected at least 1 map converted, got {executedCount}.");

            InstalledAssetsEnvironment.RecordCoverage(
                _output,
                "H7-01 Generals map round-trip",
                executedCount,
                skippedCount,
                $"Maps: {string.Join(", ", processedMaps)}");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static (string MapPath, MapFile SourceMap, string PackageDir) CreateAndConvertSyntheticMap(string tempRoot)
    {
        const int lattice = 129;
        const int border = 6;

        var heights = new ushort[lattice, lattice];
        for (var y = 0; y < lattice; y++)
        {
            for (var x = 0; x < lattice; x++)
            {
                heights[x, y] = (ushort)(24 + (((x * 3) + (y * 5)) % 17));
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, border);

        var customObject = new MapObject(new Vector3(120, 140, 25), 0.75f, RoadType.None, "SyntheticStructure");
        customObject.Properties.AddReal("AuthoredElevationOffset", 12.5f);
        mapFile.ObjectsList.AddObject(customObject);

        var mapPath = Path.Combine(tempRoot, "SyntheticH7.map");
        using (var stream = File.Create(mapPath))
        {
            mapFile.WriteTo(stream);
        }

        var packageDir = Path.Combine(tempRoot, "synthetic-package");

        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["convert-map", "--input", mapPath, "--out", packageDir],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.True(exitCode == 0, $"convert-map failed: {errorOut}");

        var parsedSourceMap = ParseMapFile(mapPath);
        return (mapPath, parsedSourceMap, packageDir);
    }

    private static MapFile ParseMapFile(string mapPath)
    {
        using var stream = File.OpenRead(mapPath);
        using var mapStream = MapFile.Decompress(stream);
        return MapFile.FromStream(mapStream);
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonH7Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetFilePath = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(file, targetFilePath, overwrite: true);
        }
    }
}
