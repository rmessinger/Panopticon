#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>
/// Round-trip validator tests for WI-H5-03 in map mode: identical content passes, each map failure
/// class is reported with its named class, the provenance allowlist permits only its enumerated rules,
/// and the JSON report parses with stable field names.
/// </summary>
[Collection("SerialGameTests")]
public sealed class H5MigrateRoundTripMapTests
{
    [Fact]
    public void ValidateMap_IdenticalContent_Passes()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.True(report.Passed, string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => f.Message)));
            Assert.Equal("map", report.Mode);
            Assert.All(report.Sections, section => Assert.True(section.Passed));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateMap_FlippedTypedValue_FailsAsTypedFieldMismatch()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
            Assert.NotNull(manifest);

            var tampered = manifest! with { Identity = manifest.Identity! with { Name = "Tampered" } };
            File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(tampered));

            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.FailureClass == RoundTripFailureClass.TypedFieldMismatch);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateMap_CorruptedRetainedByte_FailsAsRetainedHashMismatch()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var entry = RetainedEntry(packageDir, BlendTileData.AssetName);
            var filePath = Path.Combine(packageDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(filePath);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(filePath, bytes);

            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.FailureClass == RoundTripFailureClass.RetainedHashMismatch);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateMap_DeletedRetainedFile_FailsAsMissingSection()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var entry = RetainedEntry(packageDir, BlendTileData.AssetName);
            File.Delete(Path.Combine(packageDir, entry.Path.Replace('/', Path.DirectorySeparatorChar)));

            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.FailureClass == RoundTripFailureClass.MissingSection);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateMap_AddedWaypoint_FailsAsExtraRecord()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
            Assert.NotNull(manifest);

            var planar = new MapPackageQuantity(0, nameof(MapQuantityUnit.GameUnit), nameof(MapQuantityFrame.MapLocalXy));
            var vertical = new MapPackageQuantity(0, nameof(MapQuantityUnit.GameUnit), nameof(MapQuantityFrame.MapLocalZ));
            var extra = new MapPackageWaypoint("tamper-extra", "Tamper", planar, planar, vertical);
            var tampered = manifest! with { Waypoints = (manifest.Waypoints ?? []).Append(extra).ToArray() };
            File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(tampered));

            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.FailureClass == RoundTripFailureClass.ExtraRecord);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateMap_DroppedWaypoint_FailsAsMissingRecord()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
            var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(manifestPath));
            Assert.NotNull(manifest);
            Assert.NotEmpty(manifest!.Waypoints ?? []);

            var tampered = manifest with { Waypoints = (manifest.Waypoints ?? []).Skip(1).ToArray() };
            File.WriteAllText(manifestPath, MapPackageManifestJson.Serialize(tampered));

            var report = ValidateSynthetic(mapPath, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.FailureClass == RoundTripFailureClass.MissingRecord);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("waypoints count 2 differs from re-read 1.", RoundTripFailureClass.MissingRecord)]
    [InlineData("objects count 0 differs from re-read 3.", RoundTripFailureClass.ExtraRecord)]
    [InlineData("retained section count 9 differs from re-read 8.", RoundTripFailureClass.MissingSection)]
    [InlineData("chunk [0] heights sample count 4 differs from re-read 5.", RoundTripFailureClass.TypedFieldMismatch)]
    public void ClassifyMap_CountMessages_MapToNamedClasses(string message, RoundTripFailureClass expected)
    {
        var finding = RoundTripFailureClassifier.ForMap(new MapPackageDiagnostic(H5MigrateCliDiagnostics.TypedMismatch, message));

        Assert.Equal(expected, finding.FailureClass);
    }

    [Fact]
    public void ClassifyMap_SourceSectionGap_IsMissingSection()
    {
        var finding = RoundTripFailureClassifier.ForMap(
            new MapPackageDiagnostic(H5MigrateCliDiagnostics.SourceSectionGap, "Retained section 'BlendTileData' is absent."));

        Assert.Equal(RoundTripFailureClass.MissingSection, finding.FailureClass);
        Assert.Equal(H5MigrateCliDiagnostics.SourceSectionGap, finding.Code);
    }

    [Theory]
    [InlineData("C:\\maps\\a.ini", "C:/maps/a.ini", "generals", null, "generals", 4, true)]
    [InlineData("a/b.ini", "a/b.ini", "generals", null, "generals", 4, true)]
    [InlineData("a/b.ini", "a/b.ini", "generals", "Base", null, 4, true)]
    [InlineData("a/b.ini", "a/b.ini", "generals", "Base", "Patch", 4, false)]
    [InlineData("a/b.ini", "a/c.ini", "generals", null, null, 4, false)]
    [InlineData("a/b.ini", "a/b.ini", "other", null, null, 4, false)]
    [InlineData("a/b.ini", "a/b.ini", "generals", null, null, 5, false)]
    public void ProvenanceAllowlist_PermitsOnlyEnumeratedRules(
        string expectedPath,
        string actualPath,
        string actualSource,
        string? expectedLayer,
        string? actualLayer,
        int actualLine,
        bool permitted)
    {
        var expected = new Panopticon.Content.Importing.Units.Provenance("generals", expectedPath, 4, expectedLayer);
        var actual = new Panopticon.Content.Importing.Units.Provenance(actualSource, actualPath, actualLine, actualLayer);

        Assert.Equal(permitted, RoundTripProvenanceAllowlist.Permits(expected, actual));
    }

    [Fact]
    public void ValidateMap_JsonReport_ParsesWithStableFieldNames()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var entry = RetainedEntry(packageDir, BlendTileData.AssetName);
            File.Delete(Path.Combine(packageDir, entry.Path.Replace('/', Path.DirectorySeparatorChar)));

            var report = ValidateSynthetic(mapPath, packageDir);
            using var document = JsonDocument.Parse(RoundTripReportWriter.ToJson(report));
            var root = document.RootElement;

            Assert.Equal("map", root.GetProperty("mode").GetString());
            Assert.False(root.GetProperty("passed").GetBoolean());

            var sections = root.GetProperty("sections");
            Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("section").GetString() == "retained");

            var findings = sections.EnumerateArray().SelectMany(section => section.GetProperty("findings").EnumerateArray()).ToList();
            Assert.Contains(findings, finding => finding.GetProperty("failureClass").GetString() == nameof(RoundTripFailureClass.MissingSection));
            Assert.All(findings, finding => Assert.False(string.IsNullOrEmpty(finding.GetProperty("code").GetString())));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateCli_MapIdenticalContent_ExitsZeroWithTextVerdict()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["validate", "--input", mapPath, "--package", packageDir],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, errorOut.ToString());
            Assert.Contains("verdict: identical", standardOut.ToString(), StringComparison.Ordinal);
            Assert.Contains("section retained: PASS", standardOut.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateCli_MapTamperedPackage_ExitsOneWithJsonVerdict()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (mapPath, packageDir) = ConvertSyntheticMap(tempRoot);
            var entry = RetainedEntry(packageDir, BlendTileData.AssetName);
            File.Delete(Path.Combine(packageDir, entry.Path.Replace('/', Path.DirectorySeparatorChar)));

            using var standardOut = new StringWriter();
            using var errorOut = new StringWriter();

            var exitCode = H5MigrateCli.Run(
                ["validate", "--input", mapPath, "--package", packageDir, "--json"],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(standardOut.ToString());
            Assert.False(document.RootElement.GetProperty("passed").GetBoolean());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateCli_MapWithRosterAndInput_IsUsageFailure()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["validate", "--input", "/tmp/a.map", "--package", "/tmp/pkg", "--p6b"],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("usage: h5migrate validate", errorOut.ToString(), StringComparison.Ordinal);
    }

    [GameFact(SageGame.CncGenerals)]
    public void ValidateMap_InstalledLooseMap_ConvertsAndValidatesIdentical()
    {
        var mapsRoot = Path.Combine(Environment.GetEnvironmentVariable("CNC_GENERALS_PATH")!, "maps");
        Assert.True(Directory.Exists(mapsRoot), $"Installed maps directory '{mapsRoot}' does not exist.");

        var candidates = Directory.EnumerateFiles(mapsRoot, "*.map", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        var tempRoot = NewTempRoot();

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
                        continue;
                    }
                }

                using var standardOut = new StringWriter();
                using var errorOut = new StringWriter();

                var exitCode = H5MigrateCli.Run(
                    ["validate", "--input", mapPath, "--package", packageDir, "--json"],
                    standardOut,
                    errorOut,
                    CancellationToken.None);

                Assert.True(exitCode == 0, $"validate failed for '{mapPath}' (exit {exitCode}): {errorOut}\n{standardOut}");
                using var document = JsonDocument.Parse(standardOut.ToString());
                Assert.True(document.RootElement.GetProperty("passed").GetBoolean());
                return;
            }

            Assert.Fail($"No loose installed map under '{mapsRoot}' converted.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static IEnumerable<RoundTripFinding> AllFindings(RoundTripReport report)
    {
        return report.Sections.SelectMany(section => section.Findings);
    }

    private static MapPackageRetainedEntry RetainedEntry(string packageDir, string sectionName)
    {
        var (manifest, _) = MapPackageManifestJson.Parse(File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));
        return Assert.Single(manifest!.RetainedSections, section => section.SectionName == sectionName);
    }

    private static RoundTripReport ValidateSynthetic(string mapPath, string packageDir)
    {
        return RoundTripValidator.ValidateMap(ParseMapFile(mapPath), mapPath, packageDir);
    }

    private static (string MapPath, string PackageDir) ConvertSyntheticMap(string tempRoot)
    {
        var mapPath = WriteSyntheticMapFile(tempRoot);
        var packageDir = Path.Combine(tempRoot, "package");

        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = H5MigrateCli.Run(
            ["convert-map", "--input", mapPath, "--out", packageDir],
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.True(exitCode == 0, $"convert-map failed: {errorOut}");
        return (mapPath, packageDir);
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static string WriteSyntheticMapFile(string tempRoot)
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
        var mapPath = Path.Combine(tempRoot, "Synthetic.map");

        using (var stream = File.Create(mapPath))
        {
            mapFile.WriteTo(stream);
        }

        return mapPath;
    }

    private static MapFile ParseMapFile(string mapPath)
    {
        using var stream = File.OpenRead(mapPath);
        using var mapStream = MapFile.Decompress(stream);
        return MapFile.FromStream(mapStream);
    }
}
