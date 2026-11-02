#nullable enable

using System;
using System.Linq;
using System.Text;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.Reporting;
using Xunit;
using static Panopticon.Geospatial.Tests.Pipeline.PlanTestSupport;
using static Panopticon.Geospatial.Tests.Pipeline.ReportBuilderTestSupport;

namespace Panopticon.Geospatial.Tests.Pipeline;

public class ImportReportBuilderTests
{
    [Fact]
    public void Build_RoundtripPreservesDiagnosticsChecksShaAndSeparateSamplings()
    {
        var (plan, output) = GeneratedOutput();
        var runtime = new GeoDiagnosticBag();
        runtime.Add(new GeoDiagnostic("GEO-X-001", GeoStage.Generate, GeoSeverity.Warning, "Cells outside the extent were clipped.", "./inputs//dem.tif"));
        runtime.Add(new GeoDiagnostic("GEO-X-002", GeoStage.Parse, GeoSeverity.Error, "No imagery band matched.", null, new InvalidOperationException("runtime secret")));
        var checks = new[]
        {
            new PayloadValidationCheck("payload", "terrain-hash", PayloadValidationStatus.Passed, 1d, 1d, 0d, "meters"),
            new PayloadValidationCheck("control-point", "corner-height", PayloadValidationStatus.Failed, 10d, 12.5d, 0.5d, "meters", "The corner height drifted."),
            new PayloadValidationCheck("payload", "imagery-png", PayloadValidationStatus.NotEvaluated),
        };

        var report = ImportReportBuilder.Build(output, plan, Sha256("payload"), runtime.Items, checks);
        var roundtripped = Roundtrip(report);

        Assert.Equal(GeneratedMapPayloadReport.ReportKind, roundtripped.Kind);
        Assert.Equal(Sha256("payload"), roundtripped.PayloadSha256Hex);
        Assert.Equal(report.Diagnostics, roundtripped.Diagnostics);
        Assert.Equal("inputs/dem.tif", roundtripped.Diagnostics[0].SourcePath);
        Assert.Equal(report.ValidationChecks, roundtripped.ValidationChecks);
        Assert.Equal(
            [PayloadValidationStatus.Passed, PayloadValidationStatus.Failed, PayloadValidationStatus.NotEvaluated],
            roundtripped.ValidationChecks.Select(static check => check.Status).ToArray());

        // The three samplings stay separate through the roundtrip: terrain counts
        // lattice samples, imagery every channel of every pixel, classification every
        // core cell.
        Assert.Equal(output.Terrain.Sampling.SampleCount, roundtripped.Terrain.Sampling.SampleCount);
        Assert.Equal(output.Imagery!.Sampling.SampleCount, roundtripped.Imagery!.Sampling.SampleCount);
        Assert.Equal(output.ClassificationSampling!.SampleCount, roundtripped.ClassificationSampling!.SampleCount);
        Assert.NotEqual(roundtripped.Terrain.Sampling.SampleCount, roundtripped.ClassificationSampling!.SampleCount);

        // Re-serializing the read-back model reproduces the exact written bytes.
        Assert.Equal(ReportBytes(report), ReportBytes(roundtripped));
    }

    [Fact]
    public void Build_ExceptionDetailNeverSerializedAndRuntimeBagUnchanged()
    {
        var (plan, output) = GeneratedOutput();
        var detail = new InvalidOperationException("unserializable runtime secret");
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic("GEO-X-010", GeoStage.Write, GeoSeverity.Error, "Staged write failed: /etc/passwd", "/etc/passwd", detail));
        var original = bag.Items.Single();

        var report = ImportReportBuilder.Build(output, plan, Sha256("payload"), bag.Items, []);

        // The runtime bag keeps its absolute path and its live exception object.
        Assert.Same(original, bag.Items.Single());
        Assert.Same(detail, original.Detail);
        Assert.Equal("/etc/passwd", original.SourcePath);

        var serialized = Encoding.UTF8.GetString(ReportBytes(report));
        Assert.DoesNotContain("runtime secret", serialized);
        Assert.DoesNotContain("Exception", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Null(report.Diagnostics.Single().SourcePath);
    }

    [Fact]
    public void Build_PreservesDiagnosticOrderSeveritiesAndMissingPaths()
    {
        var (plan, output) = GeneratedOutput();
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic("GEO-X-020", GeoStage.Generate, GeoSeverity.Info, "Imagery omitted."));
        bag.Add(new GeoDiagnostic("GEO-X-021", GeoStage.Raster, GeoSeverity.Warning, "No-data filled from the nearest valid cell."));
        bag.Add(new GeoDiagnostic("GEO-X-022", GeoStage.Write, GeoSeverity.Error, "Publishing is blocked by earlier errors."));

        var report = ImportReportBuilder.Build(output, plan, Sha256("payload"), bag.Items, []);

        Assert.Equal(
            [GeoSeverity.Info, GeoSeverity.Warning, GeoSeverity.Error],
            report.Diagnostics.Select(static diagnostic => diagnostic.Severity).ToArray());
        Assert.Equal(["GEO-X-020", "GEO-X-021", "GEO-X-022"], report.Diagnostics.Select(static diagnostic => diagnostic.Code).ToArray());
        Assert.All(report.Diagnostics, static diagnostic => Assert.Null(diagnostic.SourcePath));
    }

    [Fact]
    public void Build_RelativeSourcePathAliasesCanonicalizedAndBadFormsDropped()
    {
        var (plan, output) = GeneratedOutput();
        var report = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            [
                new GeoDiagnostic("GEO-X-030", GeoStage.Parse, GeoSeverity.Warning, "Alias one.", "./inputs//dem.tif"),
                new GeoDiagnostic("GEO-X-031", GeoStage.Parse, GeoSeverity.Warning, "Alias two.", "rasters/sub/./x.tif"),
                new GeoDiagnostic("GEO-X-032", GeoStage.Parse, GeoSeverity.Warning, "Current directory only.", "."),
                new GeoDiagnostic("GEO-X-033", GeoStage.Parse, GeoSeverity.Warning, "Traversal.", "a/../b"),
                new GeoDiagnostic("GEO-X-034", GeoStage.Parse, GeoSeverity.Warning, "Windows separators.", "inputs\\dem.tif"),
                new GeoDiagnostic("GEO-X-035", GeoStage.Parse, GeoSeverity.Warning, "Empty.", string.Empty),
            ],
            []);

        // Dot and empty segments collapse; traversal and backslash spellings are
        // dropped rather than fabricated into a different relative path.
        Assert.Equal("inputs/dem.tif", report.Diagnostics[0].SourcePath);
        Assert.Equal("rasters/sub/x.tif", report.Diagnostics[1].SourcePath);
        Assert.Null(report.Diagnostics[2].SourcePath);
        Assert.Null(report.Diagnostics[3].SourcePath);
        Assert.Null(report.Diagnostics[4].SourcePath);
        Assert.Null(report.Diagnostics[5].SourcePath);
        Assert.Equal(6, report.Diagnostics.Count);
    }

    [Fact]
    public void Build_KnownAbsoluteInputAndManifestPathsMapToCanonicalForms()
    {
        var plan = PlanWithRuntimePaths();
        var (_, output) = GeneratedOutput();
        var report = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            [
                new GeoDiagnostic("GEO-X-040", GeoStage.Parse, GeoSeverity.Warning, "Known input.", DemFullPath),
                new GeoDiagnostic("GEO-X-041", GeoStage.Manifest, GeoSeverity.Warning, "Known manifest.", ManifestFullPath),
                new GeoDiagnostic("GEO-X-042", GeoStage.Parse, GeoSeverity.Warning, "Case variant.", "/WORK/PROJECT/INPUTS/DEM.TIF"),
            ],
            []);

        Assert.Equal("inputs/dem.tif", report.Diagnostics[0].SourcePath);
        Assert.Equal(plan.Provenance.Single(static provenance => provenance.SourceId == "dem").RelativePath, report.Diagnostics[0].SourcePath);
        Assert.Equal("import.geojson", report.Diagnostics[1].SourcePath);

        // POSIX spellings compare ordinally, so a case variant stays unrepresented.
        Assert.Null(report.Diagnostics[2].SourcePath);
    }

    [Fact]
    public void Build_UnknownAbsoluteWindowsUncAndUriPathsUnrepresented()
    {
        var plan = PlanWithRuntimePaths();
        var (_, output) = GeneratedOutput();
        var report = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            [
                new GeoDiagnostic("GEO-X-050", GeoStage.Parse, GeoSeverity.Warning, "Outside.", "/etc/shadow"),
                new GeoDiagnostic("GEO-X-051", GeoStage.Parse, GeoSeverity.Warning, "Drive.", "C:\\other\\dem.tif"),
                new GeoDiagnostic("GEO-X-052", GeoStage.Parse, GeoSeverity.Warning, "Unc.", "\\\\server\\share\\dem.tif"),
                new GeoDiagnostic("GEO-X-053", GeoStage.Parse, GeoSeverity.Warning, "Uri.", "s3://bucket/dem.tif"),
                new GeoDiagnostic("GEO-X-054", GeoStage.Parse, GeoSeverity.Warning, "File uri.", "file:///work/project/inputs/dem.tif"),
            ],
            []);

        Assert.All(report.Diagnostics, static diagnostic => Assert.Null(diagnostic.SourcePath));
    }

    [Fact]
    public void Build_UnknownAbsoluteInsideProjectRelativizedWithoutTraversal()
    {
        var plan = PlanWithRuntimePaths();
        var (_, output) = GeneratedOutput();
        var report = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            [
                new GeoDiagnostic("GEO-X-060", GeoStage.Parse, GeoSeverity.Warning, "Sibling file.", "/work/project/inputs/other.tif"),
                new GeoDiagnostic("GEO-X-061", GeoStage.Parse, GeoSeverity.Warning, "Double slash.", "/work/project/notes//readme.txt"),
                new GeoDiagnostic("GEO-X-062", GeoStage.Parse, GeoSeverity.Warning, "Escaping.", "/work/project/../secret.txt"),
                new GeoDiagnostic("GEO-X-063", GeoStage.Parse, GeoSeverity.Warning, "Prefix lookalike.", "/work/projectother/file.tif"),
            ],
            []);

        Assert.Equal("inputs/other.tif", report.Diagnostics[0].SourcePath);
        Assert.Equal("notes/readme.txt", report.Diagnostics[1].SourcePath);
        Assert.Null(report.Diagnostics[2].SourcePath);
        Assert.Null(report.Diagnostics[3].SourcePath);
    }

    [Fact]
    public void Build_OwnedStagePathsNullSourcePathWithPlaceholderTextIdenticalAcrossGuids()
    {
        var (_, output) = GeneratedOutput();
        var plan = PlanWithRuntimePaths();
        var outputDirectory = "/tmp/out";
        var firstStage = outputDirectory + "/.tmp-" + Guid.NewGuid().ToString("N");
        var secondStage = outputDirectory + "/.tmp-" + Guid.NewGuid().ToString("N");

        var first = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            StageDiagnostics(firstStage),
            [StageCheck(firstStage)],
            [outputDirectory, firstStage]);
        var second = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            StageDiagnostics(secondStage),
            [StageCheck(secondStage)],
            [outputDirectory, secondStage]);

        // Owned roots and their descendants have no manifest-relative spelling: the
        // sourcePath field stays null while the placeholder appears in message text.
        Assert.Null(first.Diagnostics[0].SourcePath);
        Assert.Null(first.Diagnostics[1].SourcePath);
        Assert.Contains("<payload>/terrain/0_0.bin", first.Diagnostics[0].Message);
        Assert.Contains("remain at '<payload>'", first.Diagnostics[1].Message);
        Assert.Contains("<payload>/masks/0_0.bin", first.ValidationChecks[0].Message!);

        // Unrelated directory text that merely starts like the owned output prefix is
        // left alone; only whole path tokens are rewritten.
        Assert.Equal("Published to /tmp/output2 unchanged.", first.Diagnostics[2].Message);

        var firstBytes = ReportBytes(first);
        var secondBytes = ReportBytes(second);

        Assert.Equal(firstBytes, secondBytes);
        Assert.DoesNotContain(firstStage, Encoding.UTF8.GetString(firstBytes));
        Assert.DoesNotContain(secondStage, Encoding.UTF8.GetString(secondBytes));
    }

    [Fact]
    public void Build_CheckIdentityNumbersUnitsAndMessagesPreserved()
    {
        var plan = PlanWithRuntimePaths();
        var (_, output) = GeneratedOutput();
        var stage = "/tmp/out/.tmp-" + Guid.NewGuid().ToString("N");
        var failed = new PayloadValidationCheck(
            "control-point",
            "corner-height",
            PayloadValidationStatus.Failed,
            10d,
            12.5d,
            0.5d,
            "meters",
            "The sampled corner drifted after " + DemFullPath + " was re-read at '" + stage + "'.");
        var quiet = new PayloadValidationCheck("payload", "imagery-png", PayloadValidationStatus.NotEvaluated);

        var report = ImportReportBuilder.Build(output, plan, Sha256("payload"), [], [failed, quiet], [stage]);
        var roundtripped = Roundtrip(report);

        Assert.Equal(2, report.ValidationChecks.Count);
        Assert.Equal(failed.Category, report.ValidationChecks[0].Category);
        Assert.Equal(failed.Name, report.ValidationChecks[0].Name);
        Assert.Equal(PayloadValidationStatus.Failed, report.ValidationChecks[0].Status);
        Assert.Equal(10d, report.ValidationChecks[0].Expected);
        Assert.Equal(12.5d, report.ValidationChecks[0].Actual);
        Assert.Equal(0.5d, report.ValidationChecks[0].Tolerance);
        Assert.Equal("meters", report.ValidationChecks[0].Units);
        Assert.Equal("The sampled corner drifted after inputs/dem.tif was re-read at '<payload>'.", report.ValidationChecks[0].Message);
        Assert.Null(report.ValidationChecks[1].Message);
        Assert.Equal(report.ValidationChecks, roundtripped.ValidationChecks);
    }

    [Fact]
    public void Build_MessageKnownPathsRelativizedUnrelatedTextPreserved()
    {
        var plan = PlanWithRuntimePaths();
        var (_, output) = GeneratedOutput();
        var message = "Hash of " + DemFullPath + " changed; manifest " + ManifestFullPath
            + " re-read; see /var/log/system.log and /work/projectother/trace.txt for context.";

        var report = ImportReportBuilder.Build(
            output,
            plan,
            Sha256("payload"),
            [new GeoDiagnostic("GEO-SRC-001", GeoStage.Manifest, GeoSeverity.Error, message, ManifestFullPath)],
            []);

        Assert.Equal(
            "Hash of inputs/dem.tif changed; manifest import.geojson re-read; see /var/log/system.log and /work/projectother/trace.txt for context.",
            report.Diagnostics[0].Message);
    }

    [Fact]
    public void Build_EmptyDiagnosticsAndChecksProduceAValidReport()
    {
        var (plan, output) = GeneratedOutput();

        var report = ImportReportBuilder.Build(output, plan, Sha256("payload"), [], []);
        var roundtripped = Roundtrip(report);

        Assert.Empty(report.Diagnostics);
        Assert.Empty(report.ValidationChecks);
        Assert.Empty(roundtripped.Diagnostics);
        Assert.Empty(roundtripped.ValidationChecks);
        Assert.Equal(ReportBytes(report), ReportBytes(roundtripped));
    }

    [Fact]
    public void Build_InvalidSeverityShaAndOwnedDirectory_Propagate()
    {
        var (plan, output) = GeneratedOutput();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImportReportBuilder.Build(
                output,
                plan,
                Sha256("payload"),
                [new GeoDiagnostic("GEO-X-090", GeoStage.Generate, (GeoSeverity)999, "Invalid severity.")],
                []));
        Assert.Throws<ArgumentException>(
            () => ImportReportBuilder.Build(output, plan, "not-a-sha", [], []));
        Assert.Throws<ArgumentException>(
            () => ImportReportBuilder.Build(output, plan, Sha256("payload"), [], [], ["relative/stage"]));
        Assert.Throws<ArgumentException>(
            () => ImportReportBuilder.Build(output, plan, Sha256("payload"), [], [], ["file:///tmp/stage"]));
    }

    private static GeoDiagnostic[] StageDiagnostics(string stage)
    {
        return
        [
            new GeoDiagnostic("GEO-PAY-053", GeoStage.Write, GeoSeverity.Warning, "Staged file retry at " + stage + "/terrain/0_0.bin.", stage + "/terrain//0_0.bin"),
            new GeoDiagnostic("GEO-PAY-054", GeoStage.Write, GeoSeverity.Warning, "Removing the staging directory failed; staged files remain at '" + stage + "'.", stage),
            new GeoDiagnostic("GEO-X-070", GeoStage.Write, GeoSeverity.Info, "Published to /tmp/output2 unchanged.", null),
        ];
    }

    private static PayloadValidationCheck StageCheck(string stage)
    {
        return new PayloadValidationCheck(
            "payload",
            "mask-bytes",
            PayloadValidationStatus.Failed,
            4096d,
            2048d,
            0d,
            "bytes",
            "Mask chunk differs at " + stage + "/masks/0_0.bin: short read.");
    }
}
