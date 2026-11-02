#nullable enable

using System;
using System.IO;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Serialization;
using Panopticon.Geospatial.Pipeline.Generation;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Pipeline.SourceLoading;
using Xunit;
using static Panopticon.Geospatial.Tests.Pipeline.ChunkGenerationTestSupport;
using static Panopticon.Geospatial.Tests.Pipeline.PlanTestSupport;

namespace Panopticon.Geospatial.Tests.Pipeline;

/// <summary>
/// Fixtures for ImportReportBuilderTests: a real ImportGenerationOutput produced by the
/// accepted chunk generation over synthetic direct-model sources (no source-file IO),
/// plus a plan whose protected input paths and full source paths model the runtime
/// values the loader carries, with the manifest one level above its inputs under
/// /work/project. Staged chunk files are written into a fixture-owned temporary
/// directory that is removed before the output is returned; report assembly never
/// reads it back.
/// </summary>
internal static class ReportBuilderTestSupport
{
    public const string ManifestFullPath = "/work/project/import.geojson";

    public const string ExtentFullPath = "/work/project/inputs/area.kml";

    public const string DemFullPath = "/work/project/inputs/dem.tif";

    /// <summary>A one-chunk generation output with imagery and classification summaries.</summary>
    public static (ImportGenerationPlan Plan, ImportGenerationOutput Output) GeneratedOutput()
    {
        var (plan, planDiagnostics) = TryPlan(withImagery: true, withClassification: true);
        Assert.False(planDiagnostics.HasErrors);
        Assert.NotNull(plan);

        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();
        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);

        return (plan!, output!);
    }

    /// <summary>The builder's runtime-path fixture plan: manifest and sources under /work/project.</summary>
    public static ImportGenerationPlan PlanWithRuntimePaths()
    {
        var manifest = ImportManifest(
        [
            new ManifestSource("extent", ManifestSourceKind.Kml, "inputs/area.kml", ManifestSourceRole.Extent),
            new ManifestSource("dem", ManifestSourceKind.Geotiff, "inputs/dem.tif", ManifestSourceRole.Elevation),
        ],
            rasterPolicy: new RasterPolicy(ImageryPixelsPerSample: 1),
            verticalPolicy: new VerticalPolicy(OriginMeters: 0d, Exaggeration: Exaggeration));

        var extent = LoadedSourceOf("extent", ManifestSourceKind.Kml, ManifestSourceRole.Extent, "inputs/area.kml", ExtentFullPath);
        var dem = LoadedSourceOf("dem", ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation, "inputs/dem.tif", DemFullPath);

        var prepared = new PreparedImportSources(
            manifest,
            Sha256("manifest"),
            [ManifestFullPath, ExtentFullPath, DemFullPath],
            [extent, dem],
            [],
            PolygonExtent(ExtentOf(1, 1)),
            new LoadedRaster(dem, Dem(1, 1, PlaneHeight)));
        var diagnostics = new GeoDiagnosticBag();
        var plan = ImportGenerationPlan.Create(prepared, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);

        return plan!;
    }

    public static LoadedSource LoadedSourceOf(
        string sourceId,
        ManifestSourceKind kind,
        ManifestSourceRole role,
        string relativePath,
        string fullPath)
    {
        return new LoadedSource(
            sourceId,
            kind,
            role,
            relativePath,
            fullPath,
            Sha256(sourceId),
            1000L + sourceId.Length,
            ExtentCrs,
            "unknown",
            null,
            "unknown");
    }

    public static byte[] ReportBytes(GeneratedMapPayloadReport report)
    {
        using var stream = new MemoryStream();

        PayloadJson.WriteReport(stream, report);

        return stream.ToArray();
    }

    public static GeneratedMapPayloadReport Roundtrip(GeneratedMapPayloadReport report)
    {
        using var stream = new MemoryStream(ReportBytes(report));
        var diagnostics = new GeoDiagnosticBag();
        var read = PayloadJson.TryReadReport(stream, diagnostics, new GeospatialImportLimits());

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(read);

        return read!;
    }
}
