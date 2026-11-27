#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Generated-bridge level layout: the installed Concrete bridge over the 700-unit route matches the
/// measured trim facts, the layout is invariant under a rigid rotation of the route, and unsupported
/// inputs return a reason instead of a layout.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBridgeLayoutTests
{
    private const double DatumZ = 1101.25d;

    private const string LeftName = "BRIDGE_LEFT";

    private const string SpanName = "BRIDGE_SPAN";

    private const string RightName = "BRIDGE_RIGHT";

    /// <summary>Tolerance on the measured doubles of the installed bridge.</summary>
    private const double FactTolerance = 1e-3;

    /// <summary>Tolerance on the cap area: float rounding of the route-local round trip (reference 980.812041).</summary>
    private const double CapAreaTolerance = 1e-2;

    /// <summary>Tolerance between the rotated and unrotated layouts for every fact except the cap area.</summary>
    private const double RotationFactTolerance = 1e-4;

    private static readonly Vector2 RouteStart = new(550f, 300f);

    private static readonly Vector2 RouteEnd = new(550f, 1000f);

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_LevelLayout_MatchesMeasuredFacts()
    {
        WithLoadedGame(game =>
        {
            var bridge = game.Scene3D.Bridges[0];
            var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, DatumZ, out var reason);

            Assert.Null(reason);
            Assert.NotNull(layout);
            var built = layout!;
            var trim = built.Trim;

            AssertClose(104.506573d, trim.LengthLeft, FactTolerance);
            AssertClose(101.713944d, trim.LengthSpan, FactTolerance);
            AssertClose(104.506592d, trim.LengthRight, FactTolerance);
            Assert.Equal(4, trim.FullSpans);
            AssertClose(84.131060d, trim.TrimmedLength, FactTolerance);
            AssertClose(17.582884d, trim.TrimAmount, FactTolerance);
            Assert.Equal(72, trim.TrimmedPieceFaces);
            Assert.Equal(39, trim.KeptIntact);
            Assert.Equal(29, trim.ClippedFaces);
            Assert.Equal(45, trim.ClippedOutputTriangles);
            Assert.Equal(4, trim.DroppedFaces);
            Assert.Equal(1, trim.CapLoops);
            Assert.Equal(0, trim.CapOpenChains);
            Assert.Equal(0, trim.CapNonManifoldNodes);
            Assert.Equal(27, trim.CapTriangles);
            AssertClose(980.812041d, trim.CapArea, CapAreaTolerance);

            Assert.Equal(535, built.Faces.Count);
            Assert.Equal(463, built.FaceKinds.Count(kind => kind == GeneratedBridgeFaceKind.Original));
            Assert.Equal(45, built.FaceKinds.Count(kind => kind == GeneratedBridgeFaceKind.Clipped));
            Assert.Equal(27, built.FaceKinds.Count(kind => kind == GeneratedBridgeFaceKind.Cap));
            Assert.Equal(30, built.Roadway.Count);

            var roadwayZ = built.Roadway.SelectMany(face => new[] { face.A.Z, face.B.Z, face.C.Z }).ToList();
            AssertClose(DatumZ, roadwayZ.Average(), FactTolerance);

            AssertClose(2.485229d, built.RoadwayStartS, FactTolerance);
            AssertClose(697.514893d, built.RoadwayEndS, FactTolerance);
            AssertClose(-33.786377d, built.RoadwayMinN, FactTolerance);
            AssertClose(32.778748d, built.RoadwayMaxN, FactTolerance);

            Assert.All(built.Faces, face => Assert.True(
                IsFinite(face.A) && IsFinite(face.B) && IsFinite(face.C), "every layout vertex must be finite."));

            // Joint between the trimmed span and the right piece, at three lateral positions in route units.
            var toLocal = built.Route.WorldToLocal;
            var local = built.Roadway.Select(face => (
                Piece: face.PieceName,
                Copy: face.CopyIndex,
                Points: new[] { face.A, face.B, face.C }.Select(vertex => Vector3.Transform(vertex, toLocal)).ToArray())).ToList();
            var right = local.Where(face => face.Piece.Contains(RightName, StringComparison.OrdinalIgnoreCase)).ToList();
            var trimmed = local.Where(face => face.Piece.Contains(SpanName, StringComparison.OrdinalIgnoreCase) && face.Copy == trim.FullSpans).ToList();

            foreach (var lateral in new[] { -30f, 0f, 30f })
            {
                var gap = MinYAtX(right, lateral) - MaxYAtX(trimmed, lateral);
                Assert.InRange(gap, -0.001d, 0.01d);
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_RotatedRoute_RotatesTheSameLayout()
    {
        WithLoadedGame(game =>
        {
            var bridge = game.Scene3D.Bridges[0];
            var input = bridge.CreateLayoutInput(RouteStart, RouteEnd, DatumZ);
            Assert.NotNull(input);

            var layoutA = GeneratedBridgeLayoutBuilder.TryBuild(input!, out var reasonA);
            Assert.Null(reasonA);
            Assert.NotNull(layoutA);

            var centre = new Vector3(550f, 650f, 0f);
            var rotation =
                Matrix4x4.CreateTranslation(-centre) *
                Matrix4x4.CreateRotationZ(MathF.PI / 6f) *
                Matrix4x4.CreateTranslation(centre);

            var rotatedInput = input! with
            {
                Route = new BridgeRoute(ToXy(Vector3.Transform(new Vector3(RouteStart, 0f), rotation)), ToXy(Vector3.Transform(new Vector3(RouteEnd, 0f), rotation))),
                AuthoredWorldMatrix = input.AuthoredWorldMatrix * rotation,
                RawFaces = input.RawFaces.Select(face => face with
                {
                    A = Vector3.Transform(face.A, rotation),
                    B = Vector3.Transform(face.B, rotation),
                    C = Vector3.Transform(face.C, rotation),
                }).ToList(),
            };

            var layoutB = GeneratedBridgeLayoutBuilder.TryBuild(rotatedInput, out var reasonB);
            Assert.Null(reasonB);
            Assert.NotNull(layoutB);

            var a = layoutA!;
            var b = layoutB!;
            AssertSameFacts(a.Trim, b.Trim);
            Assert.Equal(a.FaceKinds, b.FaceKinds);
            Assert.Equal(a.Faces.Count, b.Faces.Count);

            var largest = 0f;

            for (var index = 0; index < a.Faces.Count; index++)
            {
                var expected = new[]
                {
                    Vector3.Transform(a.Faces[index].A, rotation),
                    Vector3.Transform(a.Faces[index].B, rotation),
                    Vector3.Transform(a.Faces[index].C, rotation),
                };
                var actual = new[] { b.Faces[index].A, b.Faces[index].B, b.Faces[index].C };

                for (var vertex = 0; vertex < 3; vertex++)
                {
                    largest = MathF.Max(largest, Vector3.Distance(expected[vertex], actual[vertex]));
                }
            }

            Assert.True(largest <= 1e-2f, $"rotated layout differs by {largest} at a vertex.");
        });
    }

    [Fact]
    public void TryBuild_RouteShorterThanEndPieces_ReturnsReason()
    {
        var input = PureInput(new Vector2(0f, 0f), new Vector2(100f, 0f), 60d, 10d, 60d, [Face(LeftName, 0, new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f))]);

        Assert.Null(GeneratedBridgeLayoutBuilder.TryBuild(input, out var reason));
        Assert.Equal("route shorter than the two end pieces", reason);
    }

    [Fact]
    public void TryBuild_DegenerateRoute_ReturnsReason()
    {
        var input = PureInput(new Vector2(5f, 5f), new Vector2(5f, 5f), 100d, 200d, 100d, [Face(LeftName, 0, new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f))]);

        Assert.Null(GeneratedBridgeLayoutBuilder.TryBuild(input, out var reason));
        Assert.Equal("route is degenerate", reason);
    }

    [Fact]
    public void TryBuild_NoSpanPieceToTrim_ReturnsReason()
    {
        var input = PureInput(
            new Vector2(0f, 0f),
            new Vector2(0f, 1000f),
            100d,
            200d,
            100d,
            [
                Face(LeftName, 0, new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f)),
                Face(RightName, 0, new Vector3(-10f, 900f, 0f), new Vector3(10f, 900f, 0f), new Vector3(0f, 910f, 0f)),
            ]);

        Assert.Null(GeneratedBridgeLayoutBuilder.TryBuild(input, out var reason));
        Assert.Equal("no span piece to trim", reason);
    }

    [Fact]
    public void TryBuild_TrimmedCutDoesNotClose_ReturnsReason()
    {
        // Route 1050 with lengths 100/200/100: four full spans and a 50-unit trim. One span triangle crosses
        // the plane, so the cut is a single open segment.
        var input = PureInput(
            new Vector2(0f, 0f),
            new Vector2(0f, 1050f),
            100d,
            200d,
            100d,
            [
                Face(LeftName, 0, new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f)),
                Face(SpanName, 3, new Vector3(-10f, 900f, 0f), new Vector3(10f, 980f, 0f), new Vector3(10f, 900f, 0f)),
                Face(RightName, 0, new Vector3(-10f, 900f, 0f), new Vector3(10f, 900f, 0f), new Vector3(0f, 910f, 0f)),
            ]);

        Assert.Null(GeneratedBridgeLayoutBuilder.TryBuild(input, out var reason));
        Assert.Equal("the trimmed piece's cut does not close", reason);
    }

    [Fact]
    public void TryBuild_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GeneratedBridgeLayoutBuilder.TryBuild(null!, out _));
    }

    private static GeneratedBridgeLayoutInput PureInput(
        Vector2 start,
        Vector2 end,
        double left,
        double span,
        double right,
        IReadOnlyList<BridgeSurface.BridgeRawFace> faces)
    {
        return new GeneratedBridgeLayoutInput
        {
            Route = new BridgeRoute(start, end),
            DatumZ = DatumZ,
            BridgeScale = 1f,
            AuthoredWorldMatrix = Matrix4x4.CreateFromQuaternion(QuaternionUtility.CreateRotation(Vector3.UnitX, Vector3.UnitY)),
            RawFaces = faces,
            LeftPieceName = LeftName,
            SpanPieceName = SpanName,
            RightPieceName = RightName,
            LengthLeft = left,
            LengthSpan = span,
            LengthRight = right,
        };
    }

    private static BridgeSurface.BridgeRawFace Face(string piece, int copy, Vector3 a, Vector3 b, Vector3 c)
    {
        return new BridgeSurface.BridgeRawFace(piece, copy, 4, 0, 0, a, b, c);
    }

    private static void AssertSameFacts(GeneratedBridgeTrimFacts expected, GeneratedBridgeTrimFacts actual)
    {
        AssertClose(expected.LengthLeft, actual.LengthLeft, RotationFactTolerance);
        AssertClose(expected.LengthSpan, actual.LengthSpan, RotationFactTolerance);
        AssertClose(expected.LengthRight, actual.LengthRight, RotationFactTolerance);
        AssertClose(expected.TrimmedLength, actual.TrimmedLength, RotationFactTolerance);
        AssertClose(expected.TrimAmount, actual.TrimAmount, RotationFactTolerance);
        AssertClose(expected.CapArea, actual.CapArea, CapAreaTolerance);
        Assert.Equal(expected.FullSpans, actual.FullSpans);
        Assert.Equal(expected.TrimmedPieceFaces, actual.TrimmedPieceFaces);
        Assert.Equal(expected.KeptIntact, actual.KeptIntact);
        Assert.Equal(expected.ClippedFaces, actual.ClippedFaces);
        Assert.Equal(expected.ClippedOutputTriangles, actual.ClippedOutputTriangles);
        Assert.Equal(expected.DroppedFaces, actual.DroppedFaces);
        Assert.Equal(expected.CapLoops, actual.CapLoops);
        Assert.Equal(expected.CapOpenChains, actual.CapOpenChains);
        Assert.Equal(expected.CapNonManifoldNodes, actual.CapNonManifoldNodes);
        Assert.Equal(expected.CapTriangles, actual.CapTriangles);
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} within {tolerance} of {actual}.");
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector2 ToXy(Vector3 value) => new(value.X, value.Y);

    /// <summary>Lowest route-Y of the faces at a lateral (route X) position, as the reference joint measure.</summary>
    private static double MinYAtX(IReadOnlyList<(string Piece, int Copy, Vector3[] Points)> faces, float x)
    {
        return Extreme(faces, x, lowest: true);
    }

    /// <summary>Highest route-Y of the faces at a lateral (route X) position.</summary>
    private static double MaxYAtX(IReadOnlyList<(string Piece, int Copy, Vector3[] Points)> faces, float x)
    {
        return Extreme(faces, x, lowest: false);
    }

    private static double Extreme(IReadOnlyList<(string Piece, int Copy, Vector3[] Points)> faces, float x, bool lowest)
    {
        var best = lowest ? double.PositiveInfinity : double.NegativeInfinity;

        foreach (var face in faces)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                var a = face.Points[edge];
                var b = face.Points[(edge + 1) % 3];

                if (MathF.Abs(a.X - b.X) < 1e-6f)
                {
                    if (MathF.Abs(a.X - x) < 1e-4f)
                    {
                        best = lowest ? Math.Min(best, Math.Min(a.Y, b.Y)) : Math.Max(best, Math.Max(a.Y, b.Y));
                    }

                    continue;
                }

                if (x < Math.Min(a.X, b.X) - 1e-4f || x > Math.Max(a.X, b.X) + 1e-4f)
                {
                    continue;
                }

                var t = (x - a.X) / (double)(b.X - a.X);
                var y = a.Y + ((b.Y - a.Y) * t);
                best = lowest ? Math.Min(best, y) : Math.Max(best, y);
            }
        }

        return best;
    }

    private static void WithLoadedGame(Action<Game> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-level-layout", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            Convert(payloadDir, package, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(package, [], GameType.SinglePlayer);
            Assert.NotEmpty(game.Scene3D.Bridges);
            body(game);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Fixture helpers (copied from GeneratedPackageBridgeTraversalTests).
    // ------------------------------------------------------------------

    private static string StagePayload(string payloadDir)
    {
        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(payloadDir, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(["grass", "dirt"]);
        var descriptors = new List<PayloadChunkDescriptor>();

        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

                for (var sy = 0; sy < ChunkLayout.StoredPointsPerChunkAxis; sy++)
                {
                    for (var sx = 0; sx < ChunkLayout.StoredPointsPerChunkAxis; sx++)
                    {
                        heights[(sy * ChunkLayout.StoredPointsPerChunkAxis) + sx] =
                            PayloadHeight((cx * ChunkLayout.IntervalsPerChunk) + sx - 1, (cy * ChunkLayout.IntervalsPerChunk) + sy - 1);
                    }
                }

                var terrain = writer.WriteTerrain(new TerrainChunk(cx, cy, heights));
                var materials = writer.WriteMaterial(new MaterialChunk(cx, cy, palette, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                var masks = writer.WriteMask(new TerrainMaskChunk(cx, cy, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var bridgeEndpoints = new (double X, double Y)[] { (550d, 300d), (550d, 1000d) };
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(bridgeEndpoints[0].X, bridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(bridgeEndpoints[1].X, bridgeEndpoints[1].Y))]),
                "vectors",
                "bridge-a",
                "vectors/bridge-a#0",
                -1),
        };

        var transform = new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 0d,
            verticalExaggeration: 1d);
        var grid = new PayloadGridDefinition(
            2,
            2,
            transform,
            new GeoPolygon(new GeoRing(
            [
                new GeoPosition(new Vector2d(500000d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6000000d)),
            ])),
            null,
            new SlopeThresholds(45d, 15d));
        var metadata = new GeneratedMapPayloadMetadata(grid, palette, features, new string('7', 64), [], descriptors);
        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);

        var report = new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(SamplingSummary(), 0, 0, 0),
            new PayloadMaskGenerationSummary(45d, 15d, 1d, 4, 0, 0, 0, 0, 0, 0, 0, 0),
            new PayloadFeatureGenerationSummary(
                features.Count,
                [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, features.Count(feature => feature.Kind == kind)))]),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex);
        Assert.True(writer.WriteReport(report));
        Assert.True(writer.TryCommit());

        return payloadDir;
    }

    private static float PayloadHeight(long latticeX, long latticeY)
    {
        // Plane terrain with a north-south gorge strip the bridge crosses.
        var gorge = Math.Abs(latticeY - 64) < 4 ? -80f : 0f;

        return 1000f + (1.25f * latticeX) + (0.5f * latticeY) + gorge;
    }

    private static PayloadSamplingSummary SamplingSummary()
    {
        return new PayloadSamplingSummary(
            0,
            0,
            0,
            0,
            [.. Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, 0))],
            new PayloadObservedRanges(
                gsdRatioX: new PayloadObservedRange(0.98d, 1.02d),
                gsdRatioY: new PayloadObservedRange(0.97d, 1.03d)));
    }

    private static void Convert(string payloadDir, string packageDir, bool withRules)
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var textureMap = PreviewTextureMap.FromJson(
            """{"grass":"GrassType1","dirt":"DirtMediumType1"}""",
            new FixedTerrainTextureCatalog(["GrassType1", "DirtMediumType1"]),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPackageConverter.Convert(
            reader!,
            "r3-g8-level-layout",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
