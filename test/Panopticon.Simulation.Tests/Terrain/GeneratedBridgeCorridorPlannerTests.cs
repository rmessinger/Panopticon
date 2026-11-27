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
/// Corridor planning from original heights: measured candidate match, floored apply once, edge unsupported.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBridgeCorridorPlannerTests
{
    private static readonly Vector2 RouteStart = new(550f, 300f);

    private static readonly Vector2 RouteEnd = new(550f, 1000f);

    private static readonly (int Y, double[] Expected)[] ApproachRows =
    [
        (10, [1064.375, 1065.625, 1066.835938, 1068.085938, 1069.335938, 1070.585938, 1071.835938, 1073.085938, 1074.335938]),
        (50, [1068.984375, 1070.117188, 1071.25, 1072.382812, 1073.515625, 1074.648438, 1075.78125, 1076.875, 1078.007812]),
        (100, [1078.007812, 1078.828125, 1079.6875, 1080.507812, 1081.367188, 1082.1875, 1083.007812, 1083.867188, 1084.6875]),
        (150, [1088.125, 1088.59375, 1089.0625, 1089.570312, 1090.039062, 1090.507812, 1091.015625, 1091.484375, 1091.953125]),
        (200, [1096.679688, 1096.875, 1097.03125, 1097.1875, 1097.382812, 1097.539062, 1097.695312, 1097.890625, 1098.046875]),
        (250, [1101.09375, 1101.09375, 1101.09375, 1101.09375, 1101.09375, 1101.132812, 1101.132812, 1101.132812, 1101.132812]),
        (1050, [1101.328125, 1101.328125, 1101.328125, 1101.328125, 1101.367188, 1101.367188, 1101.367188, 1101.367188, 1101.367188]),
        (1100, [1104.414062, 1104.570312, 1104.765625, 1104.921875, 1105.078125, 1105.273438, 1105.429688, 1105.585938, 1105.78125]),
        (1150, [1110.507812, 1110.976562, 1111.445312, 1111.953125, 1112.421875, 1112.890625, 1113.398438, 1113.867188, 1114.335938]),
        (1200, [1117.773438, 1118.59375, 1119.453125, 1120.273438, 1121.09375, 1121.953125, 1122.773438, 1123.632812, 1124.453125]),
        (1250, [1124.453125, 1125.585938, 1126.679688, 1127.8125, 1128.945312, 1130.078125, 1131.210938, 1132.34375, 1133.476562]),
        (1290, [1128.125, 1129.375, 1130.625, 1131.875, 1133.125, 1134.375, 1135.625, 1136.835938, 1138.085938]),
    ];

    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_PlanMatchesTheMeasuredCandidate()
    {
        // Planned on the authored fixture: the loader must not apply the corridor before the test does.
        Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = false;
        try
        {
            WithLoadedGame(game =>
            {
                var heightMap = game.TerrainLogic.HeightMap;
                Assert.NotNull(heightMap);

                var bridge = game.Scene3D.Bridges[0];
                var datum = GeneratedBridgeCorridorPlanner.Datum(heightMap, RouteStart, RouteEnd);
                Assert.Equal(1101.25, datum, 1e-9);

                var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, datum, out var layoutReason);
                Assert.Null(layoutReason);
                Assert.NotNull(layout);

                var builtLayout = layout!;
                var plan = GeneratedBridgeCorridorPlanner.TryPlan(heightMap, builtLayout, out var reason);
                Assert.Null(reason);
                Assert.NotNull(plan);

                Assert.Equal(1101.25, plan.DatumZ, 1e-9);
                Assert.Equal(-300d, plan.ToeStartS, 1e-9);
                Assert.Equal(1000d, plan.ToeEndS, 1e-9);
                Assert.Equal(260d, plan.ApproachStart.Length, 1e-9);
                Assert.Equal(260d, plan.ApproachEnd.Length, 1e-9);
                Assert.Equal(0.17604, plan.ApproachStart.DesignMaxGrade, 0.002);
                Assert.Equal(0.17604, plan.ApproachEnd.DesignMaxGrade, 0.002);
                Assert.Equal(-43.786377, plan.LateralMin, 1e-3);
                Assert.Equal(42.778748, plan.LateralMax, 1e-3);
                Assert.Equal(2227, plan.Targets.Count);
                Assert.Equal(36, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Flush));
                Assert.Equal(72, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Bench));
                Assert.Equal(468, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Approach));
                Assert.Equal(603, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.UnderDeckCut));
                Assert.Equal(1048, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Shoulder));
                Assert.All(plan.Targets.Where(t => t.Region != GeneratedBridgeCorridorRegion.Shoulder), t => Assert.InRange(heightMap.GetPosition(t.X, t.Y).X, 510f, 590f));

                var bench = plan.Targets.Where(t => t.Region == GeneratedBridgeCorridorRegion.Bench).ToList();
                Assert.All(bench, t => Assert.Equal(1101.25, t.Height, 1e-9));

                var flush = plan.Targets.Where(t => t.Region == GeneratedBridgeCorridorRegion.Flush).ToList();
                Assert.All(flush, t => Assert.Equal(1101.25 - GeneratedBridgeCorridorPlanner.FlushSeamClearance, t.Height, 1e-3));

                foreach (var target in plan.Targets)
                {
                    if (target.Region != GeneratedBridgeCorridorRegion.UnderDeckCut)
                    {
                        continue;
                    }

                    var y = heightMap.GetPosition(target.X, target.Y).Y;

                    if (y >= 960f && y <= 970f)
                    {
                        Assert.Equal(datum - GeneratedBridgeCorridorPlanner.UnderDeckClearance, target.Height, 1e-9);
                    }

                    if (y >= 980f && y < 990f)
                    {
                        Assert.Equal(datum - GeneratedBridgeCorridorPlanner.FirstCutRowClearance, target.Height, 1e-9);
                    }

                    if (y >= 320f && y < 330f)
                    {
                        Assert.Equal((double)heightMap.GetHeight(target.X, target.Y), target.Height, 1e-9);
                    }
                }

                var byLattice = plan.Targets.ToDictionary(t => (t.X, t.Y), t => t.Height);

                foreach (var (y, expected) in ApproachRows)
                {
                    for (var xi = 0; xi < 9; xi++)
                    {
                        var x = 510 + (xi * 10);
                        var tile = heightMap.GetTilePosition(new Vector2(x, y));
                        Assert.NotNull(tile);

                        var key = tile!.Value;
                        Assert.True(byLattice.TryGetValue((key.X, key.Y), out var actual));

                        var difference = Math.Abs(actual - expected[xi]);
                        Assert.True(difference <= 0.08, $"approach row y={y} x={x} differs by {difference}, expected {expected[xi]} actual {actual}.");
                    }
                }

                foreach (var y in new[] { 0, 1300 })
                {
                    for (var xi = 0; xi < 9; xi++)
                    {
                        var x = 510 + (xi * 10);
                        var tile = heightMap.GetTilePosition(new Vector2(x, y));
                        Assert.NotNull(tile);

                        var key = tile!.Value;
                        Assert.True(byLattice.TryGetValue((key.X, key.Y), out var actual));
                        Assert.Equal((double)heightMap.GetHeight(key.X, key.Y), actual, 1e-6);
                    }
                }

                var shoulder = plan.Targets.Where(t => t.Region == GeneratedBridgeCorridorRegion.Shoulder).ToList();
                var shoulderRows = shoulder.GroupBy(t => heightMap.GetPosition(t.X, t.Y).Y).ToList();
                Assert.Equal(131, shoulderRows.Count);

                foreach (var group in shoulderRows)
                {
                    Assert.Equal(8, group.Count());
                }

                var shoulderXs = shoulder.Select(t => (double)heightMap.GetPosition(t.X, t.Y).X).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(new double[] { 470d, 480d, 490d, 500d, 600d, 610d, 620d, 630d }, shoulderXs);

                var byShoulder = shoulder.ToDictionary(t => (t.X, t.Y), t => t.Height);

                foreach (var y in new[] { 150, 650, 1000, 1200 })
                {
                    foreach (var x in new[] { 470, 480, 490, 500, 600, 610, 620, 630 })
                    {
                        var tile = heightMap.GetTilePosition(new Vector2(x, y));
                        Assert.NotNull(tile);

                        var key = tile!.Value;
                        Assert.True(byShoulder.TryGetValue((key.X, key.Y), out var actual));

                        var edgeX = x < 510 ? 510 : 590;
                        var edgeTile = heightMap.GetTilePosition(new Vector2(edgeX, y));
                        Assert.NotNull(edgeTile);

                        var edgeKey = edgeTile!.Value;
                        Assert.True(byLattice.TryGetValue((edgeKey.X, edgeKey.Y), out var edgeTarget));

                        var m = x < 510 ? (510 - x) / 10 : (x - 590) / 10;
                        var native = (double)heightMap.GetHeight(key.X, key.Y);
                        var expected = native + ((1d - (m / 5d)) * (edgeTarget - native));
                        Assert.Equal(expected, actual, 1e-9);
                    }
                }

                var checkedCount = 0;
                var largestExcursion = 0d;

                foreach (var target in shoulder)
                {
                    var position = heightMap.GetPosition(target.X, target.Y);
                    var edgeX = position.X < 510f ? 510 : 590;
                    var edgeTile = heightMap.GetTilePosition(new Vector2(edgeX, position.Y));
                    Assert.NotNull(edgeTile);

                    var edgeKey = edgeTile!.Value;
                    Assert.True(byLattice.TryGetValue((edgeKey.X, edgeKey.Y), out var edgeTarget));

                    var native = (double)heightMap.GetHeight(target.X, target.Y);
                    var low = Math.Min(native, edgeTarget);
                    var high = Math.Max(native, edgeTarget);
                    var excursion = target.Height < low ? low - target.Height : target.Height > high ? target.Height - high : 0d;
                    largestExcursion = Math.Max(largestExcursion, excursion);
                    checkedCount++;
                }

                Assert.True(largestExcursion <= 1e-9d, $"shoulder between native and edge: checked {checkedCount}, largest excursion {largestExcursion}.");

                var corridor = plan.ToCorridor(GeneratedBridgeCorridorPlanner.PoseBlendDistance);
                Assert.Equal(plan.Route, corridor.Route);
                Assert.Equal(plan.LateralMin - 40d, corridor.LateralMin, 1e-9);
                Assert.Equal(plan.LateralMax + 40d, corridor.LateralMax, 1e-9);
                Assert.Equal(plan.PoseLateralMin, corridor.LateralMin);
                Assert.Equal(plan.PoseLateralMax, corridor.LateralMax);
                Assert.Equal(plan.ToeStartS, corridor.ToeStartS);
                Assert.Equal(plan.ToeEndS, corridor.ToeEndS);
                Assert.Equal(GeneratedBridgeCorridorPlanner.PoseBlendDistance, corridor.BlendDistance);
            });
        }
        finally
        {
            Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = true;
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_ApplyWritesFlooredTargetsOnce()
    {
        // Planned on the authored fixture: the loader must not apply the corridor before the test does.
        Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = false;
        try
        {
            WithLoadedGame(game =>
            {
                var heightMap = game.TerrainLogic.HeightMap;
                Assert.NotNull(heightMap);

                var bridge = game.Scene3D.Bridges[0];
                var datum = GeneratedBridgeCorridorPlanner.Datum(heightMap, RouteStart, RouteEnd);
                var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, datum, out _);
                Assert.NotNull(layout);

                var plan = GeneratedBridgeCorridorPlanner.TryPlan(heightMap, layout!, out var reason);
                Assert.Null(reason);
                Assert.NotNull(plan);

                var result = GeneratedBridgeCorridorPlanner.Apply(heightMap, [plan]);
                Assert.True(result.Changed > 800, $"Changed {result.Changed}");

                var tile270 = heightMap.GetTilePosition(new Vector2(550f, 270f));
                Assert.NotNull(tile270);
                Assert.Equal(1101.25, (double)heightMap.GetHeight(tile270!.Value.X, tile270.Value.Y), 1e-6);

                var tile300 = heightMap.GetTilePosition(new Vector2(550f, 300f));
                Assert.NotNull(tile300);
                Assert.Equal(1101.210938, (double)heightMap.GetHeight(tile300!.Value.X, tile300.Value.Y), 1e-6);

                var tile970 = heightMap.GetTilePosition(new Vector2(550f, 970f));
                Assert.NotNull(tile970);
                Assert.Equal(1100.234375, (double)heightMap.GetHeight(tile970!.Value.X, tile970.Value.Y), 1e-6);

                var tile1000 = heightMap.GetTilePosition(new Vector2(550f, 1000f));
                Assert.NotNull(tile1000);
                Assert.Equal(1101.210938, (double)heightMap.GetHeight(tile1000!.Value.X, tile1000.Value.Y), 1e-6);

                var flushMismatches = new List<string>();

                foreach (var target in plan.Targets.Where(t => t.Region == GeneratedBridgeCorridorRegion.Flush))
                {
                    var stored = (double)heightMap.GetHeight(target.X, target.Y);
                    if (Math.Abs(stored - 1101.210938) > 1e-6)
                    {
                        flushMismatches.Add($"({target.X}, {target.Y}) stored {stored}");
                    }
                }

                Assert.True(flushMismatches.Count == 0, $"flush rows persist: {flushMismatches.Count} differ from 1101.210938: {string.Join("; ", flushMismatches)}.");

                var second = GeneratedBridgeCorridorPlanner.Apply(heightMap, [plan]);
                Assert.Equal(0, second.Changed);

                heightMap.RefreshNormalsInBoundedArea(result.MinX, result.MinY, result.MaxX, result.MaxY);
            });
        }
        finally
        {
            Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = true;
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void RouteTooCloseToTheMapEdge_IsUnsupported()
    {
        WithLoadedGame(game =>
        {
            var heightMap = game.TerrainLogic.HeightMap;
            Assert.NotNull(heightMap);

            var bridge = game.Scene3D.Bridges[0];
            var datum = GeneratedBridgeCorridorPlanner.Datum(heightMap, RouteStart, RouteEnd);
            var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, datum, out _);
            Assert.NotNull(layout);

            var installed = layout!;
            var shiftedRoute = new BridgeRoute(new Vector2(550f, 60f), new Vector2(550f, 760f));
            var shift = new Vector3(0f, -240f, 0f);
            var shifted = installed with
            {
                Route = shiftedRoute,
                Faces = installed.Faces.Select(face => face with { A = face.A + shift, B = face.B + shift, C = face.C + shift }).ToList(),
                Roadway = installed.Roadway.Select(face => face with { A = face.A + shift, B = face.B + shift, C = face.C + shift }).ToList(),
            };

            var plan = GeneratedBridgeCorridorPlanner.TryPlan(heightMap, shifted, out var reason);
            Assert.Null(plan);
            Assert.NotNull(reason);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_NonAlignedEndpointsPlanWithoutSnapping()
    {
        // Planned on the authored fixture: the loader must not apply the corridor before the test does.
        Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = false;
        try
        {
            WithLoadedGame(game =>
            {
                var heightMap = game.TerrainLogic.HeightMap;
                Assert.NotNull(heightMap);

                var bridge = game.Scene3D.Bridges[0];
                var datum = GeneratedBridgeCorridorPlanner.Datum(heightMap, RouteStart, RouteEnd);
                var layout = bridge.TryBuildGeneratedLayout(RouteStart, RouteEnd, datum, out _);
                Assert.NotNull(layout);

                var installed = layout!;
                var shiftedStart = new Vector2(550f, 305f);
                var shiftedEnd = new Vector2(550f, 1005f);
                var shiftedRoute = new BridgeRoute(shiftedStart, shiftedEnd);
                var shift = new Vector3(0f, 5f, 0f);
                var shifted = installed with
                {
                    Route = shiftedRoute,
                    Faces = installed.Faces.Select(face => face with { A = face.A + shift, B = face.B + shift, C = face.C + shift }).ToList(),
                    Roadway = installed.Roadway.Select(face => face with { A = face.A + shift, B = face.B + shift, C = face.C + shift }).ToList(),
                };

                var plan = GeneratedBridgeCorridorPlanner.TryPlan(heightMap, shifted, out var reason);
                Assert.Null(reason);
                Assert.NotNull(plan);

                Assert.Equal(-40d, plan.ApproachStart.BenchStartS, 1e-9);
                Assert.Equal(740d, plan.ApproachEnd.BenchStartS, 1e-9);

                var step = (double)HeightMap.HorizontalScale;
                var startMultiples = plan.ApproachStart.Length / step;
                var endMultiples = plan.ApproachEnd.Length / step;
                Assert.True(Math.Abs(startMultiples - Math.Round(startMultiples)) <= 1e-9, $"start length {plan.ApproachStart.Length} is not a lattice multiple.");
                Assert.True(Math.Abs(endMultiples - Math.Round(endMultiples)) <= 1e-9, $"end length {plan.ApproachEnd.Length} is not a lattice multiple.");
                Assert.True(plan.ApproachStart.Length >= GeneratedBridgeApproachProfile.MinimumLength - 1e-9, $"start length {plan.ApproachStart.Length}.");
                Assert.True(plan.ApproachEnd.Length >= GeneratedBridgeApproachProfile.MinimumLength - 1e-9, $"end length {plan.ApproachEnd.Length}.");
                Assert.Equal(plan.ApproachStart.BenchStartS - plan.ApproachStart.Length, plan.ApproachStart.ToeS, 1e-9);
                Assert.Equal(plan.ApproachEnd.BenchStartS + plan.ApproachEnd.Length, plan.ApproachEnd.ToeS, 1e-9);
                Assert.Equal(plan.ApproachStart.ToeS, plan.ToeStartS, 1e-9);
                Assert.Equal(plan.ApproachEnd.ToeS, plan.ToeEndS, 1e-9);

                Assert.InRange(plan.Targets.Count, 2115, 2339);
                Assert.Equal(36, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Flush));
                Assert.Equal(603, plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.UnderDeckCut));
                Assert.InRange(plan.Targets.Count(t => t.Region == GeneratedBridgeCorridorRegion.Shoulder), 995, 1101);

                Assert.True(plan.ApproachStart.DesignMaxGrade <= GeneratedBridgeApproachProfile.MaximumGrade, $"start grade {plan.ApproachStart.DesignMaxGrade}.");
                Assert.True(plan.ApproachEnd.DesignMaxGrade <= GeneratedBridgeApproachProfile.MaximumGrade, $"end grade {plan.ApproachEnd.DesignMaxGrade}.");
            });
        }
        finally
        {
            Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = true;
        }
    }

    [Fact]
    public void Apply_WithoutPlans_WritesNothing()
    {
        var heightMap = new HeightMap(HeightMapData.Create16Bit(0, new ushort[4, 4]));
        var result = GeneratedBridgeCorridorPlanner.Apply(heightMap, []);

        Assert.True(result.MinX > result.MaxX);
        Assert.Equal(0, result.Changed);
    }

    private static void WithLoadedGame(Action<Game> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-corridor-plan", Guid.NewGuid().ToString("N"));
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
            "r3-g8-corridor-plan",
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
