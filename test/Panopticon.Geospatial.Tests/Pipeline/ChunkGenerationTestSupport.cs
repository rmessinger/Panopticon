#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Pipeline.SourceLoading;
using Panopticon.Geospatial.Sampling;
using static Panopticon.Geospatial.Tests.Pipeline.PlanTestSupport;

namespace Panopticon.Geospatial.Tests.Pipeline;

/// <summary>
/// Fixtures for ImportChunkGenerationTests: synthetic interchange rasters whose cell
/// centers coincide with the map lattice (metersPerGameUnit 1, spacing 10 m), so sampled
/// values are analytically exact. Rasters cover the apron and half-spacing footprints
/// with two margin cells per side, because the target-to-source transform round trip
/// perturbs footprint edges by nanometers and the Fail edge policy must not trip on
/// round-off: DEM and classification centers -20..chunks*1280+20, imagery centers
/// -15..chunks*1280+15. The extent is one meter below the chunk span so a 1x1 grid never
/// depends on round-off absorption.
/// </summary>
internal static class ChunkGenerationTestSupport
{
    public const double Exaggeration = 2d;

    public static double PlaneHeight(double x, double y) => 10d + (0.01d * x) + (0.02d * y);

    public static double FlatHeight(double x, double y) => 10d;

    /// <summary>Extent rectangle one meter below the whole-chunk span of the given grid.</summary>
    public static GeoPolygon ExtentOf(int chunksX, int chunksY)
    {
        return TmRectangle(0d, 0d, (chunksX * ChunkLayout.ChunkSpanGameUnits) - 1d, (chunksY * ChunkLayout.ChunkSpanGameUnits) - 1d);
    }

    /// <summary>Analytic plane DEM whose cell centers sit exactly on lattice samples.</summary>
    public static GeospatialRaster<double> Dem(int chunksX, int chunksY, Func<double, double, double> height)
    {
        var cellsX = (chunksX * ChunkLayout.IntervalsPerChunk) + 5;
        var cellsY = (chunksY * ChunkLayout.IntervalsPerChunk) + 5;
        var transform = new AffineTransform(-25d, 10d, 0d, (chunksY * ChunkLayout.ChunkSpanGameUnits) + 25d, 0d, -10d);
        var samples = new double[cellsX * cellsY];

        for (var row = 0; row < cellsY; row++)
        {
            for (var column = 0; column < cellsX; column++)
            {
                var world = transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d));
                samples[(row * cellsX) + column] = height(world.X, world.Y);
            }
        }

        return new GeospatialRaster<double>(
            cellsX,
            cellsY,
            1,
            transform,
            ExtentCrs,
            samples,
            sampleFormat: RasterSampleFormat.Float64);
    }

    /// <summary>Three-band uint8 imagery whose cell centers sit exactly on output pixel centers.</summary>
    public static GeospatialRaster<double> Imagery(int chunksX, int chunksY)
    {
        var cells = (chunksX * ChunkLayout.IntervalsPerChunk) + 4;
        var cellsY = (chunksY * ChunkLayout.IntervalsPerChunk) + 4;
        var transform = new AffineTransform(-20d, 10d, 0d, (chunksY * ChunkLayout.ChunkSpanGameUnits) + 20d, 0d, -10d);
        var samples = new double[cells * cellsY * 3];

        for (var row = 0; row < cellsY; row++)
        {
            for (var column = 0; column < cells; column++)
            {
                var world = transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d));
                var offset = ((row * cells) + column) * 3;
                samples[offset] = ((int)(world.X / 10d)) % 251;
                samples[offset + 1] = ((int)(world.Y / 10d)) % 251;
                samples[offset + 2] = 60d;
            }
        }

        return new GeospatialRaster<double>(
            cells,
            cellsY,
            3,
            transform,
            ExtentCrs,
            samples,
            sampleFormat: RasterSampleFormat.UInt8);
    }

    /// <summary>Categorical raster whose cell centers sit exactly on core lattice samples.</summary>
    public static GeospatialRaster<double> Classification(int chunksX, int chunksY, Func<double, double, int> classOf)
    {
        var cellsX = (chunksX * ChunkLayout.IntervalsPerChunk) + 5;
        var cellsY = (chunksY * ChunkLayout.IntervalsPerChunk) + 5;
        var transform = new AffineTransform(-25d, 10d, 0d, (chunksY * ChunkLayout.ChunkSpanGameUnits) + 25d, 0d, -10d);
        var samples = new double[cellsX * cellsY];

        for (var row = 0; row < cellsY; row++)
        {
            for (var column = 0; column < cellsX; column++)
            {
                var world = transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d));
                samples[(row * cellsX) + column] = classOf(world.X, world.Y);
            }
        }

        return new GeospatialRaster<double>(
            cellsX,
            cellsY,
            1,
            transform,
            ExtentCrs,
            samples,
            sampleFormat: RasterSampleFormat.UInt8);
    }

    public static GeospatialFeature VectorPoint(string featureId, double x, double y)
    {
        return new GeospatialFeature(
            "vectors",
            featureId,
            new GeoPointGeometry(new GeoPosition(new Vector2d(x, y))),
            ExtentCrs);
    }

    public static GeospatialFeature VectorPolygon(string featureId, double minX, double minY, double width, double height, string? attributeKey = null, string? attributeValue = null)
    {
        var attributes = attributeKey is null
            ? null
            : new Dictionary<string, string> { [attributeKey] = attributeValue! };

        return new GeospatialFeature("vectors", featureId, TmRectangle(minX, minY, width, height), ExtentCrs, attributes);
    }

    /// <summary>Builds a plan from synthetic sources through the accepted planning card.</summary>
    public static (ImportGenerationPlan? Plan, GeoDiagnosticBag Diagnostics) TryPlan(
        int chunksX = 1,
        int chunksY = 1,
        Func<double, double, double>? demHeight = null,
        GeospatialRaster<double>? demRaster = null,
        bool withImagery = false,
        bool withClassification = false,
        Func<double, double, int>? classificationOf = null,
        IReadOnlyList<GeospatialFeature>? vectorFeatures = null,
        IReadOnlyList<MaterialRule>? materialRules = null,
        IReadOnlyList<FeatureMappingRule>? featureMappings = null)
    {
        demHeight ??= PlaneHeight;
        var manifestSources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "inputs/area.kml", ManifestSourceRole.Extent),
            new("dem", ManifestSourceKind.Geotiff, "inputs/dem.tif", ManifestSourceRole.Elevation),
        };
        var loaded = new List<LoadedSource>
        {
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml),
            Source("dem", ManifestSourceRole.Elevation),
        };
        var elevationRaster = new LoadedRaster(loaded[1], demRaster ?? Dem(chunksX, chunksY, demHeight));
        LoadedRaster? imagery = null;
        LoadedRaster? classification = null;

        if (withImagery)
        {
            manifestSources.Add(new ManifestSource("ortho", ManifestSourceKind.Geotiff, "inputs/ortho.dat", ManifestSourceRole.Imagery));
            loaded.Add(Source("ortho", ManifestSourceRole.Imagery));
            imagery = new LoadedRaster(loaded[^1], Imagery(chunksX, chunksY));
        }

        if (withClassification)
        {
            manifestSources.Add(new ManifestSource("cover", ManifestSourceKind.Geotiff, "inputs/cover.dat", ManifestSourceRole.Classification));
            loaded.Add(Source("cover", ManifestSourceRole.Classification));
            classificationOf ??= static (_, _) => 7;
            classification = new LoadedRaster(loaded[^1], Classification(chunksX, chunksY, classificationOf));
        }

        var manifest = ImportManifest(
            manifestSources,
            materialRules,
            featureMappings,
            rasterPolicy: new RasterPolicy(ImageryPixelsPerSample: 1),
            verticalPolicy: new VerticalPolicy(OriginMeters: 0d, Exaggeration: Exaggeration));
        var prepared = new PreparedImportSources(
            manifest,
            Sha256("manifest"),
            null,
            loaded,
            vectorFeatures ?? [],
            PolygonExtent(ExtentOf(chunksX, chunksY)),
            elevationRaster,
            imagery,
            classification);
        var diagnostics = new GeoDiagnosticBag();

        return (ImportGenerationPlan.Create(prepared, diagnostics), diagnostics);
    }

    public static byte[] ReadStaged(string stageDirectory, string relativePath)
    {
        return File.ReadAllBytes(StagePath(stageDirectory, relativePath));
    }

    public static string StagePath(string stageDirectory, string relativePath)
    {
        var fullPath = stageDirectory;

        foreach (var segment in relativePath.Split('/'))
        {
            fullPath = Path.Combine(fullPath, segment);
        }

        return fullPath;
    }

    public static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Real staging directory with the accepted staged-file writer; owned and removed by the fixture.</summary>
    internal sealed class StageFixture : IDisposable
    {
        private StageFixture(string stage, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
        {
            Stage = stage;
            Limits = limits;
            Files = new PayloadStagedFiles(stage, limits, diagnostics);
        }

        public string Stage { get; }

        public GeospatialImportLimits Limits { get; }

        public PayloadStagedFiles Files { get; }

        public static StageFixture Create(GeospatialImportLimits? limits = null)
        {
            var stage = Path.Combine(Path.GetTempPath(), "panopticon-chunkgen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);

            return new StageFixture(stage, limits ?? new GeospatialImportLimits(), new GeoDiagnosticBag());
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Stage, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp leftovers are acceptable; the transaction owner decides cleanup.
            }
        }
    }
}
