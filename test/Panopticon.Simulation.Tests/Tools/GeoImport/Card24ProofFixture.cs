using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Projection;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Synthetic card 24 proof project (WI-B4b-24): a 2x2-chunk UTM 31N extent with a BCL-only
/// float32 GeoTIFF DEM, a KML extent, a KML vector file with authored player starts, an
/// ordinary waypoint inside the planned crop, one waypoint outside it, and a plaza polygon
/// that paints an explicit material and impassability. Heights follow the closed-form
/// plane-plus-ridge surface <see cref="SurfaceHeight"/>, so every expectation in the tests
/// is computed analytically instead of from pipeline output. The planned preview crop is
/// the south-east chunk (<c>[1, 0, 2, 1)</c>), whose south-west origin is non-zero, so the
/// height rebase and the XY translation of the preview transform are both meaningful. All
/// files live under one disposable temp root; no HOME, install, user-data, or environment
/// mutation. Texture bindings use real Generals Terrain.ini block names (verified against
/// the installed catalog by the fixture tests when an installation is discoverable).
/// </summary>
public sealed class Card24ProofFixture : IDisposable
{
    /// <summary>Playable extent per axis: 2 chunk spans minus the outer world edge sample.</summary>
    public const double ExtentMeters = (2 * ChunkLayout.ChunkSpanGameUnits) - 1d;

    /// <summary>UTM 31N anchor of the extent south-west corner (EPSG:32631 coordinates).</summary>
    public const double Easting0 = 500000d;

    public const double Northing0 = 6000000d;

    /// <summary>
    /// DEM cells per axis: 261 10 m cells with centers at -20..2580 relative meters
    /// (corners -25..2585), covering the whole 256-interval lattice plus its one-sample
    /// aprons with one extra margin cell on every side, so no lattice sample sits on or
    /// near a source boundary.
    /// </summary>
    public const int DemCells = (2 * ChunkLayout.IntervalsPerChunk) + (2 * ChunkLayout.ApronSamplesPerSide) + 3;

    public const double DemCellSizeMeters = ChunkLayout.SampleSpacingGameUnits;

    public const double MetersPerGameUnit = 1d;

    public const double VerticalOriginMeters = 0d;

    public const double VerticalExaggeration = 1d;

    public const double RidgeCenterX = 1920d;

    public const double RidgeCenterY = 640d;

    public const double RidgeAmplitudeMeters = 24d;

    public const double RidgeSigmaMeters = 40d;

    /// <summary>
    /// Plaza polygon (explicit paved material, impassable) in relative meters, kept 5 m off
    /// the 10 m lattice so no sample sits on a ring edge; on-edge containment is unstable
    /// under the KML coordinate roundtrip.
    /// </summary>
    public const double PlazaMinX = 1595d;

    public const double PlazaMinY = 395d;

    public const double PlazaMaxX = 2005d;

    public const double PlazaMaxY = 805d;

    /// <summary>Real Generals Terrain.ini block names used as the fixture's texture bindings.</summary>
    public const string GrassTextureName = "GrassType1";

    public const string PavedTextureName = "AsphaltType1";

    /// <summary>The planned card 24 preview crop: the south-east chunk of the 2x2 grid.</summary>
    public static readonly PreviewCrop Crop = new(1, 0, 2, 1);

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private Card24ProofFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string ManifestPath => Path.Combine(Root, "manifest.json");

    public string TexturesJsonPath => Path.Combine(Root, "textures.json");

    /// <summary>SHA-256 (lowercase hex) of every written source file, keyed by manifest source id.</summary>
    public IReadOnlyDictionary<string, string> SourceSha256Hex { get; private set; } = new Dictionary<string, string>();

    public string ManifestSha256Hex { get; private set; } = string.Empty;

    public static Card24ProofFixture Create()
    {
        var fixture = new Card24ProofFixture(
            Path.Combine(Path.GetTempPath(), "opencode", "card24-proof", Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(fixture.Root);
        fixture.WriteExtentKml();
        var demRelative = fixture.WriteDem();
        fixture.WriteVectorKml();
        fixture.WriteTexturesJson();
        fixture.WriteManifest(demRelative);

        fixture.SourceSha256Hex = new Dictionary<string, string>
        {
            ["extent"] = Sha256Hex(Path.Combine(fixture.Root, "area.kml")),
            ["dem"] = Sha256Hex(Path.Combine(fixture.Root, demRelative)),
            ["vectors"] = Sha256Hex(Path.Combine(fixture.Root, "features.kml")),
        };
        fixture.ManifestSha256Hex = Sha256Hex(fixture.ManifestPath);

        return fixture;
    }

    /// <summary>Closed-form surface in relative meters: positive plane plus a Gaussian ridge.</summary>
    public static double SurfaceHeight(double relativeX, double relativeY)
    {
        var plane = 10d + (0.01d * relativeX) + (0.02d * relativeY);
        var dx = (relativeX - RidgeCenterX) / RidgeSigmaMeters;
        var dy = (relativeY - RidgeCenterY) / RidgeSigmaMeters;

        return plane + (RidgeAmplitudeMeters * Math.Exp(-0.5d * ((dx * dx) + (dy * dy))));
    }

    /// <summary>Payload game-Z of lattice sample (gx, gy): the source height through the manifest vertical policy, float32 like the payload.</summary>
    public static float ExpectedHeightAtLattice(int gx, int gy)
    {
        var gameZ = (SurfaceHeight(gx * DemCellSizeMeters, gy * DemCellSizeMeters) - VerticalOriginMeters)
            * VerticalExaggeration
            / MetersPerGameUnit;

        return (float)gameZ;
    }

    /// <summary>Analytic crop minimum over the crop's core lattice nodes, mirroring the preview builder's reduction domain.</summary>
    public static float ExpectedCropMinimumGameZ()
    {
        var minX = Crop.ChunkXLo * ChunkLayout.IntervalsPerChunk;
        var minY = Crop.ChunkYLo * ChunkLayout.IntervalsPerChunk;
        var maxX = Crop.ChunkXHi * ChunkLayout.IntervalsPerChunk;
        var maxY = Crop.ChunkYHi * ChunkLayout.IntervalsPerChunk;

        var minimum = float.MaxValue;
        for (var gy = minY; gy <= maxY; gy++)
        {
            for (var gx = minX; gx <= maxX; gx++)
            {
                minimum = Math.Min(minimum, ExpectedHeightAtLattice(gx, gy));
            }
        }

        return minimum;
    }

    /// <summary>True for lattice nodes strictly inside the authored plaza ring.</summary>
    public static bool IsPlazaLattice(int gx, int gy)
    {
        var x = gx * DemCellSizeMeters;
        var y = gy * DemCellSizeMeters;

        return x > PlazaMinX && x < PlazaMaxX && y > PlazaMinY && y < PlazaMaxY;
    }

    /// <summary>
    /// Analytic impassable cell count of the crop: the lattice nodes strictly inside the
    /// plaza ring, plus the eastern edge node column, which sits at or over the extent east
    /// bound (ExtentMeters falls between the lattice nodes 2550 and 2560), plus the
    /// southern edge node row when the crop starts at grid row zero: the grid origin
    /// anchors the extent ring's minimum projected corner, so the extent south edge lies at
    /// game y >= 0 and row gy = 0 is never strictly interior; the playable-area stage forces
    /// it impassable. The shared corner node counts once. The ridge slope stays below the
    /// default 45 degree impassable threshold.
    /// </summary>
    public static long ExpectedImpassableCellCount()
    {
        var plazaColumns = (int)Math.Floor(PlazaMaxX / DemCellSizeMeters) - (int)Math.Ceiling(PlazaMinX / DemCellSizeMeters) + 1;
        var plazaRows = (int)Math.Floor(PlazaMaxY / DemCellSizeMeters) - (int)Math.Ceiling(PlazaMinY / DemCellSizeMeters) + 1;

        var coreWidth = ((Crop.ChunkXHi - Crop.ChunkXLo) * ChunkLayout.IntervalsPerChunk) + 1;
        var coreHeight = ((Crop.ChunkYHi - Crop.ChunkYLo) * ChunkLayout.IntervalsPerChunk) + 1;

        var eastColumn = coreHeight;
        var southRow = Crop.ChunkYLo == 0 ? coreWidth : 0;
        var cornerOverlap = eastColumn > 0 && southRow > 0 ? 1 : 0;

        return (plazaColumns * plazaRows) + eastColumn + southRow - cornerOverlap;
    }

    /// <summary>Authored waypoint positions in payload game coordinates, keyed by KML name.</summary>
    public static (double X, double Y) WaypointGamePosition(string name) => name switch
    {
        "Player_1_Start" => (1500d, 300d),
        "Player_2_Start" => (2300d, 900d),
        "ridgepeak" => (RidgeCenterX, RidgeCenterY),
        "farpoint" => (300d, 2200d),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown fixture waypoint."),
    };

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
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
        var directory = Path.Combine(Root, "dem");
        Directory.CreateDirectory(directory);

        // Row 0 is the northern edge: cell centers run from 2580 (north) down to -20 (south).
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

        var demPath = Path.Combine(directory, "dem.tif");
        BclFloat32GeoTiff.Write(
            demPath,
            DemCells,
            DemCells,
            samples,
            originEastingWestCorner: Easting0 - 25d,
            originNorthingNorthCorner: Northing0 + 2585d,
            pixelSizeMeters: DemCellSizeMeters,
            projectedEpsg: 32631,
            verticalEpsg: 5773);

        return "dem/dem.tif";
    }

    private void WriteVectorKml()
    {
        var plazaRing = string.Join(
            " ",
            new[]
            {
                LonLat(PlazaMinX, PlazaMinY),
                LonLat(PlazaMaxX, PlazaMinY),
                LonLat(PlazaMaxX, PlazaMaxY),
                LonLat(PlazaMinX, PlazaMaxY),
                LonLat(PlazaMinX, PlazaMinY),
            });

        static string PointPlacemark(string id, string name, string coordinate)
        {
            return $"<Placemark id=\"{id}\"><name>{name}</name><Point><coordinates>{coordinate}</coordinates></Point></Placemark>";
        }

        var placemarks = string.Concat(
            PointPlacemark("wp1", "Player_1_Start", LonLat(1500d, 300d)),
            PointPlacemark("wp2", "Player_2_Start", LonLat(2300d, 900d)),
            PointPlacemark("ridge", "ridgepeak", LonLat(RidgeCenterX, RidgeCenterY)),
            PointPlacemark("far", "farpoint", LonLat(300d, 2200d)),
            "<Placemark id=\"plaza\"><name>plaza</name><ExtendedData><Data name=\"zone\"><value>plaza</value></Data></ExtendedData>"
            + $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{plazaRing}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>");

        File.WriteAllText(Path.Combine(Root, "features.kml"), Kml(placemarks));
    }

    private void WriteTexturesJson()
    {
        File.WriteAllText(
            TexturesJsonPath,
            $$"""
            {
              "grass": "{{GrassTextureName}}",
              "paved": "{{PavedTextureName}}"
            }
            """);
    }

    private void WriteManifest(string demRelative)
    {
        File.WriteAllText(
            ManifestPath,
            $$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{demRelative}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5, "horizontalAccuracyMeters": 0.6 },
                { "id": "vectors", "kind": "kml", "path": "features.kml", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": {{MetersPerGameUnit.ToString(CultureInfo.InvariantCulture)}},
              "defaultMaterial": "grass",
              "extentFeatureId": "playable",
              "verticalPolicy": { "originMeters": {{VerticalOriginMeters.ToString(CultureInfo.InvariantCulture)}}, "exaggeration": {{VerticalExaggeration.ToString(CultureInfo.InvariantCulture)}} },
              "featureMappings": [
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "Player_1_Start", "target": "waypoint" },
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "Player_2_Start", "target": "waypoint" },
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "ridgepeak", "target": "waypoint" },
                { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "farpoint", "target": "waypoint" },
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

    private static string Kml(string placemarks)
    {
        return $"<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>{placemarks}</Document></kml>";
    }

    private static string Sha256Hex(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
