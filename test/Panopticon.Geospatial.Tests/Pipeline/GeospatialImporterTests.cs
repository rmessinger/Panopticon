#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Pipeline.ImportOrchestration;
using Xunit;

namespace Panopticon.Geospatial.Tests.Pipeline;

public class GeospatialImporterTests
{
    [Fact]
    public void Run_CommitsAOneChunkPayloadFromRealKmlAndGeoTiffs()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();

        var result = GeospatialImporter.Run(project.ManifestPath, output);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == GeoSeverity.Error);
        Assert.Equal(output, result.PayloadPath);
        Assert.True(Directory.Exists(result.PayloadPath));
        Assert.Null(result.RetainedBackupPath);
        Assert.NotNull(result.Report);

        var readBag = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(output, readBag);

        Assert.NotNull(reader);
        Assert.False(readBag.HasErrors);

        using (reader!)
        {
            var metadata = reader!.Metadata;

            Assert.Equal(1, metadata.Grid.ChunksX);
            Assert.Equal(1, metadata.Grid.ChunksY);
            Assert.Equal(1, metadata.Grid.ImageryPixelsPerSample);
            Assert.Equal("grass", metadata.Palette.DefaultMaterial);
            Assert.Equal("shrub", metadata.Palette.Names[1]);
            Assert.Equal(
                ["extent", "dem", "ortho", "cover", "vectors"],
                metadata.Provenance.Select(provenance => provenance.SourceId).ToArray());

            Assert.Contains(metadata.Features, feature =>
                feature.Kind == GeneratedFeatureKind.Waypoint
                && feature.Position is { } position
                && Math.Abs(position.X - ImportProjectFixture.WaypointGame.X) < 1d
                && Math.Abs(position.Y - ImportProjectFixture.WaypointGame.Y) < 1d);
            Assert.Contains(metadata.Features, feature => feature.Kind == GeneratedFeatureKind.Passability);

            // Apron included: stored position (0, 0) is the lattice sample (-1, -1).
            var terrain = reader.ReadTerrain(0, 0, readBag);

            Assert.NotNull(terrain);
            Assert.Equal(
                (float)ImportProjectFixture.PlaneHeight(-10d, -10d),
                terrain!.GetHeight(0, 0));
            Assert.Equal(
                (float)ImportProjectFixture.PlaneHeight(0d, 0d),
                terrain.GetHeight(1, 1));
            Assert.Equal(
                (float)ImportProjectFixture.PlaneHeight(1280d, 1280d),
                terrain.GetHeight(129, 129));
            Assert.Equal(
                (float)ImportProjectFixture.PlaneHeight(640d, 320d),
                terrain.GetHeight(65, 33));

            var material = reader.ReadMaterial(0, 0, readBag);

            Assert.NotNull(material);
            Assert.Equal(0, material!.GetIndex(0, 0));
            Assert.Equal(1, material.GetIndex(1, 0));
            Assert.Equal(ClassIndex(10d, 10d), material.GetIndex(1, 1));
            Assert.Equal(ClassIndex(640d, 320d), material.GetIndex(64, 32));

            var mask = reader.ReadMask(0, 0, readBag);

            Assert.NotNull(mask);
            Assert.True(mask!.GetFlags(25, 25).HasFlag(TerrainMaskFlags.Impassable));
            Assert.False(mask.GetFlags(40, 40).HasFlag(TerrainMaskFlags.Impassable));

            Assert.NotNull(reader.ReadImagery(0, 0, readBag));

            var payloadJson = File.ReadAllBytes(Path.Combine(output, "payload.json"));
            Assert.Equal(Sha256Hex(payloadJson), result.Report!.PayloadSha256Hex);

            // Three separate sampling statistics, never merged.
            Assert.NotNull(result.Report.Terrain);
            Assert.NotNull(result.Report.Imagery);
            Assert.NotNull(result.Report.ClassificationSampling);
        }
    }

    [Fact]
    public void Run_ProducesByteIdenticalPayloadAndReportAcrossDifferentOutputDirectories()
    {
        using var project = ImportProjectFixture.Create();
        var first = project.OutputPath("out-a");
        var second = project.OutputPath("out-b");

        var firstResult = GeospatialImporter.Run(project.ManifestPath, first);
        var secondResult = GeospatialImporter.Run(project.ManifestPath, second, new GeoImportOptions(), CancellationToken.None);

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first, "payload.json")),
            File.ReadAllBytes(Path.Combine(second, "payload.json")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first, "report.json")),
            File.ReadAllBytes(Path.Combine(second, "report.json")));
    }

    [Fact]
    public void Run_MalformedManifestFailsWithoutCreatingAnyOutput()
    {
        using var project = ImportProjectFixture.Create(manifest: false);
        var output = project.OutputPath();

        var result = GeospatialImporter.Run(Path.Combine(project.Root, "missing.json"), output);

        Assert.False(result.Success);
        Assert.Null(result.PayloadPath);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == GeoSeverity.Error);
        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_WithoutForceFailsAndPreservesTheExistingOutput()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "operator.txt"), "operator");

        var result = GeospatialImporter.Run(project.ManifestPath, output);

        Assert.False(result.Success);
        Assert.Equal("operator", File.ReadAllText(Path.Combine(output, "operator.txt")));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_WithForceRetainsABackupOfThePreviousOutputByteExact()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(Path.Combine(output, "sub"));
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        File.WriteAllText(Path.Combine(output, "sub", "old-sub.bin"), "old-sub");
        var before = SnapshotFiles(output);

        var result = GeospatialImporter.Run(project.ManifestPath, output, new GeoImportOptions(force: true));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.NotNull(result.RetainedBackupPath);
        Assert.True(File.Exists(Path.Combine(output, "payload.json")));
        Assert.Equal(before, SnapshotFiles(result.RetainedBackupPath!));
    }

    [Fact]
    public void Run_WithForceAndAFailedCheckPreservesTheOldOutputWithoutBackup()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        var before = SnapshotFiles(output);

        var result = GeospatialImporter.Run(
            project.ManifestPath,
            output,
            new GeoImportOptions(force: true, stagedValidators: [new RecordingValidator([ValidationChecks.Failed("waypoint-z")])]));

        Assert.False(result.Success);
        Assert.Null(result.RetainedBackupPath);
        Assert.Equal(before, SnapshotFiles(output));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ImporterDiagnostics.ValidatorCheckFailed);
        Assert.NotNull(result.Report);
        Assert.Contains(result.Report!.ValidationChecks, check => check.Status == PayloadValidationStatus.Failed);
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_RejectsAnOutputContainingTheManifestEvenWithForce()
    {
        using var project = ImportProjectFixture.Create();
        var manifestBefore = File.ReadAllBytes(project.ManifestPath);

        var result = GeospatialImporter.Run(project.ManifestPath, project.Root, new GeoImportOptions(force: true));

        Assert.False(result.Success);
        Assert.Null(result.PayloadPath);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == GeoSeverity.Error);
        Assert.Equal(manifestBefore, File.ReadAllBytes(project.ManifestPath));
        Assert.True(File.Exists(Path.Combine(project.Root, "area.kml")));
    }

    [Fact]
    public void Run_RunsAllValidatorsInOrderAgainstTheSameManifestAndReader()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        var first = new RecordingValidator([ValidationChecks.Passed("first"), ValidationChecks.NotEvaluated("first-skip")]);
        var second = new RecordingValidator([ValidationChecks.Passed("second")]);

        var result = GeospatialImporter.Run(
            project.ManifestPath, output, new GeoImportOptions(stagedValidators: [first, second]));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Equal(1, first.Invocations);
        Assert.Equal(1, second.Invocations);
        Assert.Same(first.Manifests[0], second.Manifests[0]);
        Assert.Same(first.Readers[0], second.Readers[0]);
        Assert.Equal(
            ["first", "first-skip", "second"],
            result.Report!.ValidationChecks.Select(check => check.Name).ToArray());
    }

    [Fact]
    public void Run_ValidatorErrorDiagnosticBlocksEvenWithOnlyPassedChecks()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        var validator = new RecordingValidator(
            [ValidationChecks.Passed("ok")],
            (_, bag) => bag.Add(new GeoDiagnostic("GEO-VAL-099", GeoStage.Validate, GeoSeverity.Error, "The staged validator failed its own inspection.")));

        var result = GeospatialImporter.Run(project.ManifestPath, output, new GeoImportOptions(stagedValidators: [validator]));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "GEO-VAL-099");
        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_CheckBudgetOverflowFailsBeforeCopyAndStopsLaterValidators()
    {
        using var project = ImportProjectFixture.Create(vectors: false);
        var output = project.OutputPath();
        var overflow = new RecordingValidator(ValidationChecks.ManyPassed(3));
        var never = new RecordingValidator([ValidationChecks.Passed("never")]);

        var result = GeospatialImporter.Run(
            project.ManifestPath,
            output,
            new GeoImportOptions(
                limits: new GeospatialImportLimits { MaxFeatureCount = 2 },
                stagedValidators: [overflow, never]));

        Assert.False(result.Success);
        Assert.Equal(0, never.Invocations);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ImporterDiagnostics.CheckBudgetExceeded);
        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_CallbackChunkTamperingIsRejectedByTheFullRevalidation()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();

        var result = GeospatialImporter.Run(
            project.ManifestPath,
            output,
            new GeoImportOptions(stagedValidators: [new RecordingValidator([ValidationChecks.Passed("ok")], (_, _) => TamperStageFile(project, output, Path.Combine("terrain", "0_0.bin")))]));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == GeoSeverity.Error);
        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_CallbackMetadataTamperingIsRejectedByTheFullRevalidation()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();

        var result = GeospatialImporter.Run(
            project.ManifestPath,
            output,
            new GeoImportOptions(stagedValidators: [new RecordingValidator([ValidationChecks.Passed("ok")], (_, _) => TamperStageFile(project, output, "payload.json"))]));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == GeoSeverity.Error);
        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_CallbackReportTamperingIsNeutralizedByTheRewrite()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();

        var result = GeospatialImporter.Run(
            project.ManifestPath,
            output,
            new GeoImportOptions(stagedValidators: [new RecordingValidator([ValidationChecks.Passed("ok")], (_, _) => File.WriteAllText(Path.Combine(StageOf(project, output), "report.json"), "tampered"))]));

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.True(File.Exists(Path.Combine(output, "report.json")));
    }

    [Fact]
    public void Run_CancelledBeforeSources_PropagatesWithoutArtifacts()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => GeospatialImporter.Run(project.ManifestPath, output, new GeoImportOptions(), source.Token));

        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_CancelledDuringCallbacks_CleansStageAndPreservesTheOldOutput()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        var before = SnapshotFiles(output);
        using var source = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(
            () => GeospatialImporter.Run(
                project.ManifestPath,
                output,
                new GeoImportOptions(
                    force: true,
                    stagedValidators: [new RecordingValidator([ValidationChecks.Passed("ok")], (_, _) => source.Cancel())]),
                source.Token));

        Assert.Equal(before, SnapshotFiles(output));
        AssertNoStageLeftovers(project, output);
    }

    [Fact]
    public void Run_UnexpectedValidatorException_PropagatesAfterCleanup()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();

        Assert.Throws<InvalidOperationException>(
            () => GeospatialImporter.Run(
                project.ManifestPath,
                output,
                new GeoImportOptions(stagedValidators: [new RecordingValidator([ValidationChecks.Passed("ok")], (_, _) => throw new InvalidOperationException("validator bug"))])));

        Assert.False(Directory.Exists(output));
        AssertNoStageLeftovers(project, output);
    }

    private static int ClassIndex(double gameX, double gameY)
    {
        return ImportProjectFixture.ClassOf(gameX, gameY) == 20 ? 1 : 0;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>The staging directory is the unique temp sibling of the output inside the project root.</summary>
    private static string StageOf(ImportProjectFixture project, string output)
    {
        return Directory.GetDirectories(project.Root, $"{Path.GetFileName(output)}.tmp-*").Single();
    }

    private static void TamperStageFile(ImportProjectFixture project, string output, string relativePath)
    {
        using var stream = File.Open(Path.Combine(StageOf(project, output), relativePath), FileMode.Append);
        stream.WriteByte(0);
    }

    private static Dictionary<string, byte[]> SnapshotFiles(string root)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order())
        {
            snapshot[Path.GetRelativePath(root, path)] = File.ReadAllBytes(path);
        }

        return snapshot;
    }

    private static void AssertNoStageLeftovers(ImportProjectFixture project, string output)
    {
        Assert.Empty(Directory.GetDirectories(project.Root, $"{Path.GetFileName(output)}.tmp-*"));
        Assert.Empty(Directory.GetDirectories(project.Root, $"{Path.GetFileName(output)}.backup-*"));
    }
}
