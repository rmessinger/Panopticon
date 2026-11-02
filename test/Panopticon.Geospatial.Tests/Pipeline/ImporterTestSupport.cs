#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Tests.Fixtures;

namespace Panopticon.Geospatial.Tests.Pipeline;

/// <summary>
/// Real operator-style import project: an inverse-projected KML extent over a 1279 m
/// one-chunk target, real synthetic GeoTIFFs whose cell centers carry the analytic
/// plane of ChunkGenerationTestSupport (133x133 10 m DEM centers -20..1300, one margin
/// cell beyond the first and last apron centers; 132x132 imagery centers -15..1295;
/// uint8 classification), a vector KML with a waypoint point and a passability region,
/// and the manifest binding them. No epsilon, clamp, or policy relaxation; the GeoTIFF
/// fixtures are intentionally not disposed so their files live until this project is.
/// </summary>
internal sealed class ImportProjectFixture : IDisposable
{
    public const double ExtentMeters = 1279d;

    public const double Easting0 = 500000d;

    public const double Northing0 = 6000000d;

    internal static readonly (double X, double Y) WaypointGame = (100d, 100d);

    internal static readonly (double X, double Y) RegionMin = (200d, 200d);

    internal static readonly (double X, double Y) RegionMax = (300d, 300d);

    private const int DemCells = 133;

    private const int ImageryCells = 132;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private ImportProjectFixture(string root)
    {
        Root = root;
    }

    internal string Root { get; }

    internal string ManifestPath => Path.Combine(Root, "manifest.json");

    internal static ImportProjectFixture Create(
        bool imagery = true,
        bool classification = true,
        bool vectors = true,
        bool manifest = true)
    {
        var fixture = new ImportProjectFixture(
            Path.Combine(Path.GetTempPath(), "panopticon-importer-tests-" + Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(fixture.Root);
        fixture.WriteExtentKml();

        var demRelative = fixture.WriteDem();
        var imageryRelative = imagery ? fixture.WriteImagery() : null;
        var coverRelative = classification ? fixture.WriteClassification() : null;

        if (vectors)
        {
            fixture.WriteVectorKml();
        }

        if (manifest)
        {
            fixture.WriteManifest(demRelative, imageryRelative, coverRelative, vectors);
        }

        return fixture;
    }

    /// <summary>Analytic plane in relative meters; matches ChunkGenerationTestSupport.PlaneHeight.</summary>
    internal static double PlaneHeight(double x, double y)
    {
        return 10d + (0.01d * x) + (0.02d * y);
    }

    /// <summary>Classification value at a game position: 10, 20, 30, 40 cycling over 10 m cells.</summary>
    internal static int ClassOf(double x, double y)
    {
        var cell = ((int)(x / 10d)) + ((int)(y / 10d));

        return 10 * (1 + ((cell % 4) + 4) % 4);
    }

    internal string OutputPath(string name = "out")
    {
        return Path.Combine(Root, name);
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }

    internal static string LonLat(double relativeX, double relativeY)
    {
        var world = Transformer.Transform(
            new Vector2d(Easting0 + relativeX, Northing0 + relativeY),
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            CoordinateSystemId.Wgs84);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{world.X.ToString("0.###############", CultureInfo.InvariantCulture)},{world.Y.ToString("0.###############", CultureInfo.InvariantCulture)}");
    }

    private void WriteExtentKml()
    {
        var ring = string.Join(
            " ",
            new[]
            {
                LonLat(0d, 0d),
                LonLat(ExtentMeters, 0d),
                LonLat(ExtentMeters, ExtentMeters),
                LonLat(0d, ExtentMeters),
                LonLat(0d, 0d),
            });

        File.WriteAllText(
            Path.Combine(Root, "area.kml"),
            Kml($"<Placemark id=\"playable\"><name>area</name><Polygon><outerBoundaryIs><LinearRing><coordinates>{ring}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>"));
    }

    private string WriteDem()
    {
        var samples = new double[DemCells * DemCells];

        for (var row = 0; row < DemCells; row++)
        {
            for (var column = 0; column < DemCells; column++)
            {
                samples[(row * DemCells) + column] = PlaneHeight(-25d + ((column + 0.5d) * 10d), 1305d - ((row + 0.5d) * 10d));
            }
        }

        return Relative(SyntheticGeoTiffWriter.Write(
            samples,
            new SyntheticGeoTiffOptions
            {
                Width = DemCells,
                Height = DemCells,
                SampleFormat = RasterSampleFormat.Float32,
                Crs = SyntheticGeoTiffCrs.Wgs84Utm31North,
                OriginX = Easting0 - 25d,
                OriginY = Northing0 + 1305d,
                PixelSizeX = 10d,
                PixelSizeY = 10d,
                VerticalCrsEpsg = 5773,
            },
            Root));
    }

    private string WriteImagery()
    {
        var samples = new double[ImageryCells * ImageryCells * 3];

        for (var row = 0; row < ImageryCells; row++)
        {
            for (var column = 0; column < ImageryCells; column++)
            {
                var x = -20d + ((column + 0.5d) * 10d);
                var y = 1300d - ((row + 0.5d) * 10d);
                var offset = ((row * ImageryCells) + column) * 3;
                samples[offset] = ((int)(x / 10d)) % 251;
                samples[offset + 1] = ((int)(y / 10d)) % 251;
                samples[offset + 2] = 60d;
            }
        }

        return Relative(SyntheticGeoTiffWriter.Write(
            samples,
            new SyntheticGeoTiffOptions
            {
                Width = ImageryCells,
                Height = ImageryCells,
                BandCount = 3,
                SampleFormat = RasterSampleFormat.UInt8,
                Crs = SyntheticGeoTiffCrs.Wgs84Utm31North,
                OriginX = Easting0 - 20d,
                OriginY = Northing0 + 1300d,
                PixelSizeX = 10d,
                PixelSizeY = 10d,
            },
            Root));
    }

    private string WriteClassification()
    {
        var samples = new double[DemCells * DemCells];

        for (var row = 0; row < DemCells; row++)
        {
            for (var column = 0; column < DemCells; column++)
            {
                samples[(row * DemCells) + column] = ClassOf(-25d + ((column + 0.5d) * 10d), 1305d - ((row + 0.5d) * 10d));
            }
        }

        return Relative(SyntheticGeoTiffWriter.Write(
            samples,
            new SyntheticGeoTiffOptions
            {
                Width = DemCells,
                Height = DemCells,
                SampleFormat = RasterSampleFormat.UInt8,
                Crs = SyntheticGeoTiffCrs.Wgs84Utm31North,
                OriginX = Easting0 - 25d,
                OriginY = Northing0 + 1305d,
                PixelSizeX = 10d,
                PixelSizeY = 10d,
            },
            Root));
    }

    private void WriteVectorKml()
    {
        var region = string.Join(
            " ",
            new[]
            {
                LonLat(RegionMin.X, RegionMin.Y),
                LonLat(RegionMax.X, RegionMin.Y),
                LonLat(RegionMax.X, RegionMax.Y),
                LonLat(RegionMin.X, RegionMax.Y),
                LonLat(RegionMin.X, RegionMin.Y),
            });

        File.WriteAllText(
            Path.Combine(Root, "features.kml"),
            Kml(
                $"<Placemark id=\"wp\"><name>wp0</name><Point><coordinates>{LonLat(WaypointGame.X, WaypointGame.Y)}</coordinates></Point></Placemark>"
                + "<Placemark id=\"keepout\"><name>keepout</name><ExtendedData><Data name=\"zone\"><value>keep</value></Data></ExtendedData>"
                + $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{region}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>"));
    }

    private void WriteManifest(string demRelative, string? imageryRelative, string? coverRelative, bool vectors)
    {
        var imageryClause = Clause("ortho", "imagery", imageryRelative);
        var coverClause = Clause("cover", "classification", coverRelative);
        var vectorClause = vectors ? ",\n            { \"id\": \"vectors\", \"kind\": \"kml\", \"path\": \"features.kml\", \"role\": \"vector\" }" : string.Empty;
        var materialRules = coverRelative is null
            ? string.Empty
            : ",\n          \"materialRules\": [\n            { \"classificationValue\": 20, \"sourceId\": \"cover\", \"material\": \"shrub\" }\n          ]";
        var featureMappings = vectors
            ? ",\n          \"featureMappings\": [\n"
              + "            { \"sourceId\": \"vectors\", \"geometry\": \"point\", \"attribute\": \"name\", \"value\": \"wp0\", \"target\": \"waypoint\" },\n"
              + "            { \"sourceId\": \"vectors\", \"geometry\": \"polygon\", \"attribute\": \"zone\", \"value\": \"keep\", \"target\": \"passability\", \"passable\": false }\n"
              + "          ]"
            : string.Empty;

        File.WriteAllText(
            ManifestPath,
            $$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{demRelative}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5, "horizontalAccuracyMeters": 0.6 }{{imageryClause}}{{coverClause}}{{vectorClause}}
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1,
              "defaultMaterial": "grass",
              "extentFeatureId": "playable",
              "rasterPolicy": { "imageryPixelsPerSample": 1 }{{materialRules}}{{featureMappings}}
            }
            """);

        static string Clause(string id, string role, string? relativePath)
        {
            return relativePath is null ? string.Empty : $",\n            {{ \"id\": \"{id}\", \"kind\": \"geotiff\", \"path\": \"{relativePath}\", \"role\": \"{role}\" }}";
        }
    }

    private static string Relative(SyntheticGeoTiffFixture fixture)
    {
        return Path.GetFileName(Path.GetDirectoryName(fixture.FilePath)!) + "/raster.tif";
    }

    private static string Kml(string placemarks)
    {
        return $"<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>{placemarks}</Document></kml>";
    }
}

/// <summary>Ordered validator stub shared by the importer tests; no framework, per-test fault hooks.</summary>
internal sealed class RecordingValidator : IStagedPayloadValidator
{
    private readonly IReadOnlyList<PayloadValidationCheck> _checks;

    internal RecordingValidator(
        IReadOnlyList<PayloadValidationCheck>? checks = null,
        Action<IGeneratedMapPayloadReader, GeoDiagnosticBag>? onValidate = null)
    {
        _checks = checks ?? [];
        OnValidate = onValidate;
    }

    internal Action<IGeneratedMapPayloadReader, GeoDiagnosticBag>? OnValidate { get; }

    internal List<IGeneratedMapPayloadReader> Readers { get; } = [];

    internal List<GeospatialImportManifest> Manifests { get; } = [];

    internal int Invocations => Manifests.Count;

    public IReadOnlyList<PayloadValidationCheck> Validate(
        IGeneratedMapPayloadReader reader,
        GeospatialImportManifest manifest,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        Readers.Add(reader);
        Manifests.Add(manifest);
        OnValidate?.Invoke(reader, diagnostics);

        return _checks;
    }
}

internal static class ValidationChecks
{
    internal static PayloadValidationCheck Passed(string name)
    {
        return new PayloadValidationCheck("importer-test", name, PayloadValidationStatus.Passed, message: "Matched");
    }

    internal static PayloadValidationCheck NotEvaluated(string name)
    {
        return new PayloadValidationCheck("importer-test", name, PayloadValidationStatus.NotEvaluated);
    }

    internal static PayloadValidationCheck Failed(string name)
    {
        return new PayloadValidationCheck("importer-test", name, PayloadValidationStatus.Failed, expected: 1d, actual: 2d, tolerance: 0.5d, message: "Mismatched");
    }

    internal static List<PayloadValidationCheck> ManyPassed(int count)
    {
        return Enumerable.Range(0, count).Select(index => Passed($"check-{index}")).ToList();
    }
}
