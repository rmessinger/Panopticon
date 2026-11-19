using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Triangulate.Polygon;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Geospatial.Preview.Tests;

// Gate test for WP1021 group 4: NetTopologySuite 2.6.0 PolygonTriangulator must be
// byte-deterministic across fresh geometry instances and hole-correct, first on
// representative polygons, then on the real Sedan footprint payload. Spike: any
// determinism or hole-correctness failure stops the gate; it is not repaired here.
public sealed class BuildingTriangulationSpikeTests
{
    private const string SedanPayloadPath =
        "/mnt/pit2/panopticon/geo-proof/r2-03-footprints-payload-v2/payload.json";

    private const double RelativeAreaTolerance = 1e-6;

    private readonly ITestOutputHelper _output;

    public BuildingTriangulationSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PolygonTriangulatorIsDeterministicAndHoleCorrectOnRepresentativeFixtures()
    {
        var fixtures = new List<PolygonCase>
        {
            new(
                "convex-square",
                new[] { (0d, 0d), (10d, 0d), (10d, 10d), (0d, 10d) },
                []),
            new(
                "concave-l-shape",
                new[] { (0d, 0d), (10d, 0d), (10d, 4d), (4d, 4d), (4d, 10d), (0d, 10d) },
                []),
            new("one-hole", Outer20(), new List<(double X, double Y)[]> { HoleA() }),
            new("two-holes", Outer20(), new List<(double X, double Y)[]> { HoleA(), HoleB() }),
            new(
                "reversed-winding",
                Reverse(Outer20()),
                new List<(double X, double Y)[]> { Reverse(HoleA()) }),
            new(
                "tiny-scale",
                new[] { (0d, 0d), (1e-6, 0d), (1e-6, 1e-6), (0d, 1e-6) },
                []),
        };

        foreach (var fixture in fixtures)
        {
            AssertDeterministicAndHoleCorrect(fixture, verbose: true);
        }

        var multi = AssertMultiPolygonDeterministicAndHoleCorrect(
            "multi-polygon-parts",
            new List<PolygonCase>
            {
                new(
                    "part-concave-l",
                    new[] { (0d, 0d), (10d, 0d), (10d, 4d), (4d, 4d), (4d, 10d), (0d, 10d) },
                    []),
                new(
                    "part-holed-square",
                    new[] { (30d, 0d), (50d, 0d), (50d, 20d), (30d, 20d) },
                    new List<(double X, double Y)[]> { new[] { (37d, 7d), (37d, 13d), (43d, 13d), (43d, 7d) } }),
            });

        _output.WriteLine($"multi-polygon-parts: triangles={multi}");
        _output.WriteLine($"REPRESENTATIVE FIXTURES: {fixtures.Count + 1}/{fixtures.Count + 1} passed determinism, area, and hole checks");
    }

    [Fact]
    public void PolygonTriangulatorIsDeterministicAndHoleCorrectOnSedanFootprintCorpus()
    {
        if (!File.Exists(SedanPayloadPath))
        {
            _output.WriteLine($"SEDAN PAYLOAD ABSENT at {SedanPayloadPath}; corpus gate not executed; representative fixtures carry the gate.");
            return;
        }

        var (cases, stats) = ParsePayloadPolygonCases(SedanPayloadPath);
        _output.WriteLine(
            $"SEDAN PAYLOAD: {SedanPayloadPath}; polygon features={cases.Count}; unique building footprints={stats.UniqueBuildingFootprints}; "
            + "holes=" + stats.Holes + "; skipped degenerate rings=" + stats.SkippedRings);

        var triangleTotal = 0;
        foreach (var polygonCase in cases)
        {
            triangleTotal += AssertDeterministicAndHoleCorrect(polygonCase, verbose: false);
        }

        _output.WriteLine(
            $"SEDAN CORPUS: {cases.Count} footprints triangulated, {triangleTotal} triangles; "
            + "all passed determinism, area, and hole checks.");
    }

    private int AssertDeterministicAndHoleCorrect(PolygonCase polygonCase, bool verbose)
    {
        // Fresh Coordinate instances per call, so the two triangulations see independent
        // geometry objects as a consumer of the converter would build them.
        var built1 = BuildPolygon(polygonCase.Outer, polygonCase.Holes, polygonCase.Label);
        var built2 = BuildPolygon(polygonCase.Outer, polygonCase.Holes, polygonCase.Label);

        var triangles1 = Triangulate(built1.Polygon, polygonCase.Label);
        var triangles2 = Triangulate(built2.Polygon, polygonCase.Label);

        var flat1 = FlatTriangleCoordinates(triangles1);
        var flat2 = FlatTriangleCoordinates(triangles2);
        AssertBitwiseEqual(polygonCase.Label, flat1, flat2);
        Assert.Equal(Sha256Hex(flat1), Sha256Hex(flat2));

        AssertHoleCorrectness(polygonCase.Label, built1, triangles1);

        if (verbose)
        {
            _output.WriteLine($"{polygonCase.Label}: triangles={triangles1.Count} hash={Sha256Hex(flat1)}");
        }

        return triangles1.Count;
    }

    private int AssertMultiPolygonDeterministicAndHoleCorrect(string label, IReadOnlyList<PolygonCase> parts)
    {
        var built1 = BuildMultiPolygon(parts, label);
        var built2 = BuildMultiPolygon(parts, label);

        var triangles1 = Triangulate(built1.Multi, label);
        var triangles2 = Triangulate(built2.Multi, label);

        var flat1 = FlatTriangleCoordinates(triangles1);
        var flat2 = FlatTriangleCoordinates(triangles2);
        AssertBitwiseEqual(label, flat1, flat2);
        Assert.Equal(Sha256Hex(flat1), Sha256Hex(flat2));

        var totalTriangleArea = 0d;
        foreach (var triangle in triangles1)
        {
            totalTriangleArea += triangle.Area;
            var centroid = triangle.Centroid;
            Assert.True(built1.Multi.Covers(centroid), $"{label}: triangle centroid {centroid} not covered by the multi polygon");
            foreach (var hole in built1.Holes)
            {
                Assert.False(hole.Covers(centroid), $"{label}: triangle centroid {centroid} lies inside a hole");
            }
        }

        var relativeError = RelativeAreaError(totalTriangleArea, built1.Multi.Area);
        Assert.True(
            relativeError <= RelativeAreaTolerance,
            $"{label}: triangle area {totalTriangleArea} vs multi area {built1.Multi.Area} (relative error {relativeError})");

        return triangles1.Count;
    }

    private static (Polygon Polygon, Polygon[] Holes) BuildPolygon(
        IReadOnlyList<(double X, double Y)> outer,
        IReadOnlyList<(double X, double Y)[]> holes,
        string label)
    {
        var shellCoordinates = ToClosedRing(outer);
        Assert.True(shellCoordinates is { Length: >= 4 }, $"{label}: outer ring has fewer than three distinct vertices");

        var holeRings = new LinearRing[holes.Count];
        var holePolygons = new Polygon[holes.Count];
        for (var i = 0; i < holes.Count; i++)
        {
            var holeCoordinates = ToClosedRing(holes[i]);
            Assert.True(holeCoordinates is { Length: >= 4 }, $"{label}: hole ring {i} has fewer than three distinct vertices");
            holeRings[i] = GeometryFactory.Default.CreateLinearRing(holeCoordinates);
            holePolygons[i] = GeometryFactory.Default.CreatePolygon(holeRings[i]);
        }

        var shell = GeometryFactory.Default.CreateLinearRing(shellCoordinates);
        var polygon = GeometryFactory.Default.CreatePolygon(shell, holeRings);
        var validator = new IsValidOp(polygon);
        Assert.True(validator.IsValid, $"{label}: polygon is invalid: {validator.ValidationError?.Message}");
        return (polygon, holePolygons);
    }

    private static (MultiPolygon Multi, Polygon[] Holes) BuildMultiPolygon(IReadOnlyList<PolygonCase> parts, string label)
    {
        var polygons = new Polygon[parts.Count];
        var holes = new List<Polygon>(parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var built = BuildPolygon(parts[i].Outer, parts[i].Holes, $"{label}/{parts[i].Label}");
            polygons[i] = built.Polygon;
            holes.AddRange(built.Holes);
        }

        var multi = GeometryFactory.Default.CreateMultiPolygon(polygons);
        var validator = new IsValidOp(multi);
        Assert.True(validator.IsValid, $"{label}: multi polygon is invalid: {validator.ValidationError?.Message}");
        return (multi, holes.ToArray());
    }

    private static IReadOnlyList<Polygon> Triangulate(Geometry geometry, string label)
    {
        var triangulation = PolygonTriangulator.Triangulate(geometry);
        Assert.True(triangulation.NumGeometries > 0, $"{label}: triangulation produced no triangles");

        var triangles = new List<Polygon>(triangulation.NumGeometries);
        for (var i = 0; i < triangulation.NumGeometries; i++)
        {
            var triangle = Assert.IsType<Polygon>(triangulation.GetGeometryN(i));
            Assert.True(triangle.NumPoints == 4, $"{label}: triangle {i} has {triangle.NumPoints} points instead of a closed 3-vertex ring");
            Assert.True(triangle.Area > 0d, $"{label}: triangle {i} has zero area");
            triangles.Add(triangle);
        }

        return triangles;
    }

    private static void AssertHoleCorrectness(string label, (Polygon Polygon, Polygon[] Holes) built, IReadOnlyList<Polygon> triangles)
    {
        var polygon = built.Polygon;
        var totalTriangleArea = 0d;
        foreach (var triangle in triangles)
        {
            totalTriangleArea += triangle.Area;
            var centroid = triangle.Centroid;
            Assert.True(polygon.Covers(centroid), $"{label}: triangle centroid {centroid} is not covered by the polygon");
            foreach (var hole in built.Holes)
            {
                Assert.False(hole.Covers(centroid), $"{label}: triangle centroid {centroid} lies inside a hole");
            }
        }

        var relativeError = RelativeAreaError(totalTriangleArea, polygon.Area);
        Assert.True(
            relativeError <= RelativeAreaTolerance,
            $"{label}: triangle area {totalTriangleArea} vs polygon area {polygon.Area} (relative error {relativeError})");
    }

    private static double RelativeAreaError(double totalTriangleArea, double referenceArea)
    {
        if (referenceArea <= 0d)
        {
            return double.PositiveInfinity;
        }

        return Math.Abs(totalTriangleArea - referenceArea) / referenceArea;
    }

    private static double[] FlatTriangleCoordinates(IReadOnlyList<Polygon> triangles)
    {
        var values = new List<double>(triangles.Count * 8);
        foreach (var triangle in triangles)
        {
            var ring = triangle.ExteriorRing.Coordinates;
            Assert.True(ring.Length == 4, "A triangulation triangle's exterior ring must be a closed 4-coordinate sequence");
            foreach (var coordinate in ring)
            {
                values.Add(coordinate.X);
                values.Add(coordinate.Y);
            }
        }

        return values.ToArray();
    }

    private static void AssertBitwiseEqual(string label, double[] expected, double[] actual)
    {
        Assert.True(expected.Length == actual.Length, $"{label}: triangle coordinate count differs: {expected.Length} vs {actual.Length}");
        for (var i = 0; i < expected.Length; i++)
        {
            var expectedBits = BitConverter.DoubleToInt64Bits(expected[i]);
            var actualBits = BitConverter.DoubleToInt64Bits(actual[i]);
            Assert.True(
                expectedBits == actualBits,
                $"{label}: coordinate {i / 2} component {i % 2} differs bitwise: {expectedBits:X16} vs {actualBits:X16}");
        }
    }

    private static string Sha256Hex(double[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            var bits = BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(values[i]));
            bits.CopyTo(bytes, i * 8);
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static Coordinate[]? ToClosedRing(IReadOnlyList<(double X, double Y)> positions)
    {
        // Mirrors FeatureClipper.ToRingCoordinates: consecutive duplicates and the closing
        // position are representation details and are dropped.
        var opened = new List<Coordinate>(positions.Count);
        foreach (var (x, y) in positions)
        {
            var coordinate = new Coordinate(x, y);
            if (opened.Count == 0 || !opened[^1].Equals2D(coordinate))
            {
                opened.Add(coordinate);
            }
        }

        if (opened.Count > 1 && opened[0].Equals2D(opened[^1]))
        {
            opened.RemoveAt(opened.Count - 1);
        }

        if (opened.Count < 3)
        {
            return null;
        }

        opened.Add(opened[0]);
        return opened.ToArray();
    }

    private static (List<PolygonCase> Cases, CorpusStats Stats) ParsePayloadPolygonCases(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var items = document.RootElement.GetProperty("features").GetProperty("items");

        var cases = new List<PolygonCase>();
        var uniqueBuildingFootprints = new HashSet<string>();
        var holes = 0;
        var skippedRings = 0;
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            index++;
            var geometry = item.GetProperty("geometry");
            if (geometry.GetProperty("type").GetString() != "polygon")
            {
                skippedRings++;
                continue;
            }

            var outer = ParseRingPositions(geometry.GetProperty("outerRing"));
            if (ToClosedRing(outer) is null)
            {
                skippedRings++;
                continue;
            }

            var innerRings = new List<(double X, double Y)[]>();
            foreach (var hole in geometry.GetProperty("innerRings").EnumerateArray())
            {
                var holePositions = ParseRingPositions(hole);
                if (ToClosedRing(holePositions) is null)
                {
                    skippedRings++;
                    continue;
                }

                innerRings.Add(holePositions);
                holes++;
            }

            var sourceId = item.GetProperty("sourceId").GetString() ?? string.Empty;
            var featureId = item.GetProperty("featureId").GetString() ?? string.Empty;
            if (sourceId == "sedan-proof-buildings")
            {
                uniqueBuildingFootprints.Add(featureId);
            }

            cases.Add(new PolygonCase($"{index}:{sourceId}/{featureId}", outer, innerRings));
        }

        return (cases, new CorpusStats(cases.Count, uniqueBuildingFootprints.Count, holes, skippedRings));
    }

    private static (double X, double Y)[] ParseRingPositions(JsonElement ring)
    {
        var positions = ring.GetProperty("positions");
        var parsed = new List<(double X, double Y)>(positions.GetArrayLength());
        foreach (var position in positions.EnumerateArray())
        {
            parsed.Add((position.GetProperty("x").GetDouble(), position.GetProperty("y").GetDouble()));
        }

        return parsed.ToArray();
    }

    private static (double X, double Y)[] Outer20() => new[] { (0d, 0d), (20d, 0d), (20d, 20d), (0d, 20d) };

    private static (double X, double Y)[] HoleA() => new[] { (7d, 7d), (7d, 13d), (13d, 13d), (13d, 7d) };

    private static (double X, double Y)[] HoleB() => new[] { (15d, 3d), (15d, 6d), (18d, 6d), (18d, 3d) };

    private static (double X, double Y)[] Reverse((double X, double Y)[] ring)
    {
        var reversed = new (double X, double Y)[ring.Length];
        for (var i = 0; i < ring.Length; i++)
        {
            reversed[i] = ring[ring.Length - 1 - i];
        }

        return reversed;
    }

    private sealed record PolygonCase(string Label, (double X, double Y)[] Outer, List<(double X, double Y)[]> Holes);

    private sealed record CorpusStats(int Polygons, int UniqueBuildingFootprints, int Holes, int SkippedRings);
}
