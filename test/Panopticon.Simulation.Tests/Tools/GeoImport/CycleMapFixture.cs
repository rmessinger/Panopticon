using System;
using System.Globalization;
using System.IO;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Projection;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Synthetic 2x2-chunk generated-map source set mirroring the Card24ProofFixture
/// derivation: closed-form plane DEM off the resident height grid, KML extent, waypoints
/// and plaza, plus two authored building footprints carrying cleabs/hauteur. The
/// cleabs-stripped variant keeps every polygon but drops the cleabs attribute, so the
/// converter generates no building layer.
/// </summary>
internal sealed class CycleMapFixture : IDisposable
{
    public const double ExtentMeters = (2 * ChunkLayout.ChunkSpanGameUnits) - 1d;

    private const double Easting0 = 500000d;

    private const double Northing0 = 6000000d;

    private const int DemCells = (2 * ChunkLayout.IntervalsPerChunk) + (2 * ChunkLayout.ApronSamplesPerSide) + 3;

    private const double DemCellSizeMeters = ChunkLayout.SampleSpacingGameUnits;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private CycleMapFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string ManifestPath => Path.Combine(Root, "manifest.json");

    public string ManifestWithoutCleabsPath => Path.Combine(Root, "manifest-no-buildings.json");

    public static CycleMapFixture Create()
    {
        var fixture = new CycleMapFixture(
            Path.Combine(Path.GetTempPath(), "opencode", "r3-g4-render-cycle-fixture", Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(fixture.Root);
        fixture.WriteExtentKml();
        fixture.WriteDem();
        fixture.WriteVectorKml(includeBuildingCleabs: true, "features.kml");
        fixture.WriteVectorKml(includeBuildingCleabs: false, "features-no-buildings.kml");
        fixture.WriteManifest("features.kml", fixture.ManifestPath);
        fixture.WriteManifest("features-no-buildings.kml", fixture.ManifestWithoutCleabsPath);

        return fixture;
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }

    private static double SurfaceHeight(double relativeX, double relativeY) =>
        10d + (0.005d * relativeX) + (0.003d * relativeY);

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
            Kml("<Placemark id=\"playable\"><name>area</name><Polygon><outerBoundaryIs><LinearRing><coordinates>"
                + ring
                + "</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>"));
    }

    private void WriteDem()
    {
        var directory = Path.Combine(Root, "dem");
        Directory.CreateDirectory(directory);

        var samples = new float[DemCells * DemCells];
        for (var row = 0; row < DemCells; row++)
        {
            for (var column = 0; column < DemCells; column++)
            {
                var relativeX = -20d + (column * DemCellSizeMeters);
                var relativeY = 2580d - (row * DemCellSizeMeters);
                samples[(row * DemCells) + column] = (float)SurfaceHeight(relativeX, relativeY);
            }
        }

        BclFloat32GeoTiff.Write(
            Path.Combine(directory, "dem.tif"),
            DemCells,
            DemCells,
            samples,
            originEastingWestCorner: Easting0 - 25d,
            originNorthingNorthCorner: Northing0 + 2585d,
            pixelSizeMeters: DemCellSizeMeters,
            projectedEpsg: 32631,
            verticalEpsg: 5773);
    }

    private void WriteVectorKml(bool includeBuildingCleabs, string fileName)
    {
        var placemarks = string.Concat(
            PointPlacemark("wp1", "Player_1_Start", LonLat(1500d, 300d)),
            PointPlacemark("wp2", "Player_2_Start", LonLat(2300d, 900d)),
            PolygonPlacemark(
                "plaza",
                "plaza",
                "<ExtendedData><Data name=\"zone\"><value>plaza</value></Data></ExtendedData>",
                SquareRing(1595d, 395d, 2005d, 805d)),
            BuildingPlacemark("BATTEST0000000001", 18d, SquareRing(100d, 100d, 220d, 160d), includeBuildingCleabs),
            BuildingPlacemark("BATTEST0000000002", 24d, SquareRing(1400d, 1400d, 1500d, 1480d), includeBuildingCleabs));

        File.WriteAllText(Path.Combine(Root, fileName), Kml(placemarks));
    }

    private static string BuildingPlacemark(string cleabs, double heightMeters, string ring, bool includeCleabs)
    {
        var extendedData = includeCleabs
            ? "<ExtendedData>"
                + $"<Data name=\"cleabs\"><value>{cleabs}</value></Data>"
                + $"<Data name=\"hauteur\"><value>{heightMeters.ToString(CultureInfo.InvariantCulture)}</value></Data>"
                + "</ExtendedData>"
            : string.Empty;

        return $"<Placemark id=\"{cleabs}\"><name>{cleabs}</name>{extendedData}"
            + $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{ring}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>";
    }

    private static string SquareRing(double minX, double minY, double maxX, double maxY) =>
        string.Join(
            " ",
            new[]
            {
                LonLat(minX, minY),
                LonLat(maxX, minY),
                LonLat(maxX, maxY),
                LonLat(minX, maxY),
                LonLat(minX, minY),
            });

    private static string PointPlacemark(string id, string name, string coordinate) =>
        $"<Placemark id=\"{id}\"><name>{name}</name><Point><coordinates>{coordinate}</coordinates></Point></Placemark>";

    private static string PolygonPlacemark(string id, string name, string extendedData, string ring) =>
        $"<Placemark id=\"{id}\"><name>{name}</name>{extendedData}"
        + $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{ring}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>";

    private void WriteManifest(string vectorsFileName, string manifestPath)
    {
        File.WriteAllText(
            manifestPath,
            $$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "dem/dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5, "horizontalAccuracyMeters": 0.6 },
                { "id": "vectors", "kind": "kml", "path": "{{vectorsFileName}}", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1,
              "defaultMaterial": "grass",
              "extentFeatureId": "playable",
              "verticalPolicy": { "originMeters": 0, "exaggeration": 1 },
              "featureMappings": [
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "Player_1_Start", "target": "waypoint" },
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "Player_2_Start", "target": "waypoint" },
                { "sourceId": "vectors", "geometry": "polygon", "attribute": "zone", "value": "plaza", "target": "material", "material": "paved" },
                { "sourceId": "vectors", "geometry": "polygon", "attribute": "zone", "value": "plaza", "target": "passability", "passable": false }
              ]
            }
            """);
    }

    private static string LonLat(double relativeX, double relativeY)
    {
        var world = Transformer.Transform(
            new Vector2d(Easting0 + relativeX, Northing0 + relativeY),
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            CoordinateSystemId.Wgs84);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{world.X.ToString("0.###############", CultureInfo.InvariantCulture)},{world.Y.ToString("0.###############", CultureInfo.InvariantCulture)}");
    }

    private static string Kml(string placemarks) =>
        $"<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>{placemarks}</Document></kml>";
}
