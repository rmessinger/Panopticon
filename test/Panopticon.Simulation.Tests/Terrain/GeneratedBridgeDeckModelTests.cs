#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
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
/// Level deck model: the rendered deck is the layout's triangles, a level placement surfaces the roadway and
/// stands its towers on the deck, and the authored placement is unchanged by the two-phase construction.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBridgeDeckModelTests
{
    private const double DatumZ = 1101.25d;

    private static readonly Vector2 RouteStart = new(550f, 300f);

    private static readonly Vector2 RouteEnd = new(550f, 1000f);

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_DeckModelTrianglesAreTheLayoutTriangles()
    {
        WithLoadedGame(game =>
        {
            var bridge = game.Scene3D.Bridges[0];
            var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, DatumZ, out var reason);
            Assert.Null(reason);
            Assert.NotNull(layout);
            var built = layout!;

            using var deck = GeneratedBridgeDeckModel.Create(built, bridge.PieceSourceMeshes, game.AssetStore.LoadContext);
            var meshes = deck.Imported.SubObjects.Select(subObject => (Name: subObject.FullName, Mesh: (ImportedMesh)subObject.RenderObject)).ToList();

            Assert.Equal(built.Faces.Count, meshes.Sum(entry => entry.Mesh.Indices.Count / 3));

            foreach (var group in Enumerable.Range(0, built.Faces.Count).GroupBy(face => built.Faces[face].PieceName))
            {
                var expected = group.Select(face => TriangleKey(built.Faces[face].A, built.Faces[face].B, built.Faces[face].C)).OrderBy(key => key, StringComparer.Ordinal).ToList();
                var mesh = meshes.Single(entry => entry.Name == group.Key).Mesh;
                var actual = Enumerable.Range(0, mesh.Indices.Count / 3)
                    .Select(triangle => TriangleKey(
                        mesh.Vertices[mesh.Indices[3 * triangle]].Position,
                        mesh.Vertices[mesh.Indices[(3 * triangle) + 1]].Position,
                        mesh.Vertices[mesh.Indices[(3 * triangle) + 2]].Position))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList();

                Assert.Equal(expected, actual);
            }

            foreach (var entry in meshes)
            {
                foreach (var part in entry.Mesh.MeshParts)
                {
                    Assert.InRange(part.MaterialIndex, 0, entry.Mesh.Materials.Count - 1);
                    Assert.NotNull(part.TextureCoordinates0);
                    Assert.Equal(entry.Mesh.Vertices.Count, part.TextureCoordinates0!.Count);
                }
            }

            Assert.Equal(Matrix4x4.Identity, deck.Instance.GetWorldMatrix());
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledConcrete_LevelPlacement_SurfacesTheRoadway_AndAddsFourTowers()
    {
        WithLoadedGame(game =>
        {
            var layout = game.Scene3D.Bridges[0].TryBuildGeneratedLayout(RouteStart, RouteEnd, DatumZ, out var reason);
            Assert.Null(reason);
            Assert.NotNull(layout);
            var built = layout!;

            var (startObject, endObject) = FindBridgePair(game);
            using var bridge = new Bridge(game.Scene3D.GameEngine, startObject, startObject.Position, endObject.Position);
            Assert.False(bridge.IsPlaced);

            var towerNames = TowerNames(game);
            var towersBefore = CountTowers(game, towerNames);

            var corridor = GeneratedBridgeCorridorPlanner.TryPlan(game.TerrainLogic.HeightMap, built, out var corridorReason);
            Assert.NotNull(corridor);
            bridge.PlaceLevel(built, corridor!);

            Assert.True(bridge.IsPlaced);
            Assert.Empty(bridge.Meshes);
            Assert.Empty(bridge.GetRawWorldFaces());

            var surface = bridge.CreateSurface();
            Assert.NotNull(surface);
            Assert.Equal(built.Roadway.Count, surface!.TriangleCount);

            var minX = built.Roadway.Min(face => face.MinXy.X);
            var maxX = built.Roadway.Max(face => face.MaxXy.X);
            var minY = built.Roadway.Min(face => face.MinXy.Y);
            var maxY = built.Roadway.Max(face => face.MaxXy.Y);

            for (var i = 0; i <= 4; i++)
            {
                for (var j = 0; j <= 4; j++)
                {
                    var x = minX + ((maxX - minX) * i / 4f);
                    var y = minY + ((maxY - minY) * j / 4f);
                    var point = new Vector2(x, y);
                    // The surface is defined on the triangles, so the expectation uses triangle containment:
                    // XY bounds over-approximate a sloped triangle and would report heights where there is no deck.
                    var expected = built.Roadway
                        .Where(face => ContainsXy(face, x, y))
                        .Select(face => face.PlaneZ(x, y))
                        .Where(z => float.IsFinite(z))
                        .DefaultIfEmpty(float.NaN)
                        .Max();
                    var actual = surface.GetSurfaceZ(point.X, point.Y);

                    if (float.IsNaN(expected))
                    {
                        Assert.Null(actual);
                    }
                    else
                    {
                        Assert.NotNull(actual);
                        Assert.True(MathF.Abs(actual!.Value - expected) <= 1e-3f, $"surface {actual} vs roadway plane {expected} at ({x}, {y}).");
                    }
                }
            }

            Assert.Equal(towersBefore + 4, CountTowers(game, towerNames));

            game.Render();
            Assert.Equal(Matrix4x4.Identity, bridge.DeckModel.Instance.GetWorldMatrix());
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void AuthoredPlacement_MatchesTheLoaderPlacedBridge()
    {
        // Planned on the authored fixture: the loader must not apply the corridor before the test does.
        Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = false;
        try
        {
            WithLoadedGame(game =>
            {
                var loader = game.Scene3D.Bridges[0];
                var (startObject, endObject) = FindBridgePair(game);
                using var hand = new Bridge(game.Scene3D.GameEngine, startObject, startObject.Position, endObject.Position);
                hand.PlaceAuthored();

                Assert.Equal(loader.StartPositionWithHeight, hand.StartPositionWithHeight);
                Assert.Equal(loader.EndPositionWithHeight, hand.EndPositionWithHeight);
                Assert.Equal(loader.Meshes.Count, hand.Meshes.Count);

                var loaderFaces = loader.GetRawWorldFaces();
                var handFaces = hand.GetRawWorldFaces();
                Assert.Equal(loaderFaces.Count, handFaces.Count);
                Assert.Equal(loaderFaces[0], handFaces[0]);
            });
        }
        finally
        {
            Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = true;
        }
    }

    [Fact]
    public void Create_RejectsMismatchedAttributes()
    {
        var face = new BridgeSurface.BridgeRawFace("BRIDGE_SPAN", 0, 0, 0, 0, Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var attribute = new GeneratedBridgeVertexAttributes(Vector3.UnitZ, null, null);
        var layout = new GeneratedBridgeLayout
        {
            Route = new BridgeRoute(Vector2.Zero, new Vector2(0f, 10f)),
            DatumZ = DatumZ,
            Faces = [face],
            FaceKinds = [GeneratedBridgeFaceKind.Original],
            VertexAttributes = [attribute, attribute],
            Roadway = [],
            RoadwayStartS = 0d,
            RoadwayEndS = 10d,
            RoadwayMinN = 0d,
            RoadwayMaxN = 1d,
            Trim = EmptyTrim(),
        };

        Assert.Throws<ArgumentException>(() => GeneratedBridgeDeckModel.Create(layout, new Dictionary<string, ImportedMesh>(), null!));
    }

    /// <summary>Barycentric containment of a point in the face's XY triangle, with the reference tolerance of -0.001 on each weight.</summary>
    private static bool ContainsXy(BridgeSurface.BridgeRawFace face, float x, float y)
    {
        const float tolerance = 1e-3f;
        var a = face.Axy;
        var b = face.Bxy;
        var c = face.Cxy;
        var denominator = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));

        if (denominator == 0f || !float.IsFinite(denominator))
        {
            return false;
        }

        var weightA = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / denominator;
        var weightB = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / denominator;
        var weightC = 1f - weightA - weightB;
        return weightA >= -tolerance && weightB >= -tolerance && weightC >= -tolerance;
    }

    private static GeneratedBridgeTrimFacts EmptyTrim()
    {
        return new GeneratedBridgeTrimFacts
        {
            LengthLeft = 1d,
            LengthSpan = 1d,
            LengthRight = 1d,
            TrimmedLength = 0d,
            TrimAmount = 1d,
            CapArea = 0d,
            FullSpans = 0,
            TrimmedPieceFaces = 0,
            KeptIntact = 0,
            ClippedFaces = 0,
            ClippedOutputTriangles = 0,
            DroppedFaces = 0,
            CapLoops = 0,
            CapOpenChains = 0,
            CapNonManifoldNodes = 0,
            CapTriangles = 0,
        };
    }

    private static string TriangleKey(Vector3 a, Vector3 b, Vector3 c)
    {
        return string.Join("|", new[] { a, b, c }.Select(vertex => string.Join(",", new[] { vertex.X, vertex.Y, vertex.Z }
            .Select(component => component.ToString("R", CultureInfo.InvariantCulture)))));
    }

    /// <summary>First BridgeStart and its following BridgeEnd map object, as Scene3D pairs them.</summary>
    private static (MapObject Start, MapObject End) FindBridgePair(Game game)
    {
        var objects = game.Scene3D.MapFile.ObjectsList.Objects;

        for (var index = 0; index + 1 < objects.Length; index++)
        {
            if ((objects[index].RoadType & RoadType.PrimaryType) == RoadType.BridgeStart && objects[index + 1].RoadType.HasFlag(RoadType.BridgeEnd))
            {
                return (objects[index], objects[index + 1]);
            }
        }

        throw new InvalidOperationException("the fixture map has no bridge pair.");
    }

    private static HashSet<string> TowerNames(Game game)
    {
        var template = game.AssetStore.BridgeTemplates.GetByName("Concrete")!;

        return new HashSet<string>(StringComparer.Ordinal)
        {
            template.TowerObjectNameFromLeft.Value.Name,
            template.TowerObjectNameFromRight.Value.Name,
            template.TowerObjectNameToLeft.Value.Name,
            template.TowerObjectNameToRight.Value.Name,
        };
    }

    private static int CountTowers(Game game, HashSet<string> towerNames)
    {
        return game.Scene3D.GameObjects.Objects.Count(obj => towerNames.Contains(obj.Definition.Name));
    }

    private static void WithLoadedGame(Action<Game> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-level-deck-render", Guid.NewGuid().ToString("N"));
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
    // Fixture helpers (copied from GeneratedBridgeLayoutTests).
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
            "r3-g8-level-deck-render",
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
