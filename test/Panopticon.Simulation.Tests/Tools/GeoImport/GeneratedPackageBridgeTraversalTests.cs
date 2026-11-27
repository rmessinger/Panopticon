#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
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
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using Xunit;
using GameObject = Panopticon.Logic.Object.GameObject;
using MapInterchange = Panopticon.Content.Importing.Maps.MapInterchange;
using Player = Panopticon.Logic.Player;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1029 vehicle bridge traversal proof. The portable fact converts the WP1022 gorge
/// fixture with bridge rules, reloads it through PackageReader, and checks the graded
/// landing heights at the ramp positions. The installed fact then orders a dozer across
/// the bridge: without the bridge surface the locomotor's raw-heightmap Z drives the
/// vehicle into the gorge (G7 witness: Z 1020 against a 1109 deck), with the surface
/// registered the vehicle rides the deck within tolerance and both transitions stay
/// under 3 units of Z per logic frame.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeTraversalTests
{
    /// <summary>
    /// Operator capture root, mounted on the agent host only; absent in the MCP container.
    /// </summary>
    private const string ExternalCaptureRoot = "/mnt/pit2/Network Storage/temp";

    /// <summary>Gorge band of the fixture terrain, in game units (lattice rows 61..67).</summary>
    private const float GorgeMinY = 610f;

    private const float GorgeMaxY = 670f;

    /// <summary>
    /// Static full-span camera height: above the follow-camera void band on this Z≈1000
    /// map (empirically probed: 140 voids from unstreamed terrain, 460 renders the span,
    /// 660 shows the map-edge void) and wide enough to frame the 700-unit span.
    /// </summary>
    private const float CaptureCameraHeight = 460f;

    private const int CaptureWidth = 1280;

    private const int CaptureHeight = 800;

    /// <summary>
    /// Capture budget per angle (~26 frames): denser in the landing transition bands
    /// (every 5 logic frames while y < 340 or y > 960), every 15 mid-span, so the entry
    /// and exit steps get ~5x the frame density of the span middle.
    /// </summary>
    private const int MaxCaptureCount = 26;

    private const int MaxCaptureFrames = 2400;

    /// <summary>GIF playback at 4 fps: per-frame delay in centiseconds (250 ms).</summary>
    private const int GifFrameDelayCentiseconds = 25;

    private static readonly (double X, double Y)[] BridgeEndpoints = [(550d, 300d), (550d, 1000d)];

    private const string DozerDefinitionName = "AmericaVehicleDozer";

    [Fact]
    public void UnsupportedTemplatePackage_ReloadCarriesGradedLandingHeights()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            // ConcreteOneLane is not the supported template, so the converter still grades
            // the landing band and no path-plan handoff marker is written.
            Convert(payloadDir, package, withRules: true, bridgeTemplate: "ConcreteOneLane");

            var model = new PackageReader(package).Read();
            var latticeWidth = model.Layout.LatticeWidth;

            // Endpoint (550, 300) = lattice (55, 30): terrain 1083.75, deck 1090.25, ramp
            // target 1089.25. The endpoint cell keeps its height; the cells strictly south
            // of it ramp up with the smoothstep curve.
            Assert.Equal(1083.75f, ReloadedLatticeHeight(model, latticeWidth, 55, 30));

            var currentAt29 = ResidentGridHeight(PayloadHeight(55, 29));
            var graded29 = Graded(currentAt29, 1083.75d + 6.5d, 10d);
            Assert.Equal(ResidentGridHeight(graded29), ReloadedLatticeHeight(model, latticeWidth, 55, 29));

            var currentAt28 = ResidentGridHeight(PayloadHeight(55, 28));
            var graded28 = Graded(currentAt28, 1083.75d + 6.5d, 20d);
            Assert.Equal(ResidentGridHeight(graded28), ReloadedLatticeHeight(model, latticeWidth, 55, 28));

            // Ramp edge (40 game units) and outside the cross-width band stay untouched.
            Assert.Equal(ResidentGridHeight(PayloadHeight(55, 25)), ReloadedLatticeHeight(model, latticeWidth, 55, 25));
            Assert.Equal(ResidentGridHeight(PayloadHeight(49, 29)), ReloadedLatticeHeight(model, latticeWidth, 49, 29));
            Assert.Equal(ResidentGridHeight(PayloadHeight(61, 29)), ReloadedLatticeHeight(model, latticeWidth, 61, 29));

            // The north landing ramps symmetrically toward its deck (terrain 1118.75).
            var currentAt101 = ResidentGridHeight(PayloadHeight(55, 101));
            var graded101 = Graded(currentAt101, 1118.75d + 6.5d, 10d);
            Assert.Equal(ResidentGridHeight(graded101), ReloadedLatticeHeight(model, latticeWidth, 55, 101));
            Assert.Equal(1118.75f, ReloadedLatticeHeight(model, latticeWidth, 55, 100));
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void GeneratedBridge_TerrainClearedUnderFullRoadwayFootprint()
    {
        // The authored path is the fallback for marked bridges; the original assertions are the fallback's expected values.
        Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = false;
        try
        {
            // WP1029 remedy b (r3-g8): at load time the runtime lowers terrain to the fitted
            // deck plane minus 1 under the generated bridge's actual roadway footprint (plus
            // one 10-unit transition ring), eliminating the east-edge deck burial. The gate
            // is EXACT, not grid-sampled: each terrain cell's two rendered triangles (the
            // TerrainPatch anti-diagonal (x+1,y)-(x,y+1)) are clipped against the roadway
            // inlier-triangle XY polygons; deck-plane minus terrain is linear on each clipped
            // polygon, so the minimum over the clipped vertices is the exact cell minimum.
            var scratchRoot = NewScratchRoot();

            try
            {
                var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
                var package = Path.Combine(scratchRoot, "package-on");

                Convert(payloadDir, package, withRules: true);

                var packageHashBefore = HashDirectory(package);

                var userRoot = Path.Combine(scratchRoot, "user");
                Directory.CreateDirectory(userRoot);

                using var game = new Game(
                    InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                    preferredBackend: null,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                    window: null);

                game.LoadMap(package, [], GameType.SinglePlayer);
                Assert.NotEmpty(game.Scene3D.Bridges);

                var bridge = game.Scene3D.Bridges[0];

                // Independent installed oracle (r3-g8 corrective): asset identity from the
                // verified fingerprint, raw unclassified world faces from the read-only API,
                // roadway membership from the REVIEWED source face ranges (faces 4-7 per
                // subobject — the kerb-to-kerb slab), all independent of production fit/envelope
                // classifier internals.
                Assert.False(string.IsNullOrEmpty(bridge.GeometryFingerprint), "generated bridge must report a geometry fingerprint.");
                Assert.True(BridgeSurface.IsSupportedGeometry(bridge.GeometryFingerprint), $"unexpected asset fingerprint {bridge.GeometryFingerprint}.");

                var rawFaces = bridge.GetRawWorldFaces();
                Assert.Equal(424, rawFaces.Count);

                var anchorStart = bridge.StartPositionWithHeight;
                var anchorEnd = bridge.EndPositionWithHeight;
                var oracleRoadway = rawFaces.Where(face => face.SourceFaceIndex is >= 4 and <= 7).ToList();
                Assert.Equal(24, oracleRoadway.Count);

                // Independent classification accounting over the world candidates.
                var oracleCandidates = 0;
                var oracleUnclassified = 0;
                var oracleReasons = new Dictionary<string, int>();
                var axis = anchorEnd - anchorStart;
                var axisLength = axis.Length();
                var axisDirection = axis / axisLength;
                var bandMax = (MathF.Abs(anchorEnd.Z - anchorStart.Z) * 1.5f) + 10f;

                foreach (var face in rawFaces)
                {
                    var ab = face.B - face.A;
                    var ac = face.C - face.A;
                    var normal = Vector3.Cross(ab, ac);
                    var normalLength = normal.Length();

                    if (normalLength <= float.Epsilon)
                    {
                        continue;
                    }

                    var worldNormalZ = normal.Z / normalLength;
                    var centroid = (face.A + face.B + face.C) / 3f;
                    var anchorT = Math.Clamp(Vector3.Dot(centroid - anchorStart, axisDirection) / axisLength, 0f, 1f);
                    var anchorLineZ = anchorStart.Z + ((anchorEnd.Z - anchorStart.Z) * anchorT);
                    var anchorRelativeZ = centroid.Z - anchorLineZ;

                    if (MathF.Abs(worldNormalZ) <= 0.5f || anchorRelativeZ <= -15f || anchorRelativeZ >= bandMax)
                    {
                        continue;
                    }

                    oracleCandidates++;

                    if (face.SourceFaceIndex is >= 4 and <= 7)
                    {
                        // roadway
                    }
                    else if (face.SourceFaceIndex is >= 8 and <= 23)
                    {
                        oracleReasons["kerb-rail-strip"] = oracleReasons.GetValueOrDefault("kerb-rail-strip") + 1;
                    }
                    else if (face.SourceFaceIndex is >= 24 and <= 51)
                    {
                        oracleReasons["arch-understructure"] = oracleReasons.GetValueOrDefault("arch-understructure") + 1;
                    }
                    else
                    {
                        oracleUnclassified++;
                    }
                }

                Assert.Equal(0, oracleUnclassified);
                Console.WriteLine(
                    "r3-g8 oracle accounting: rendered " + rawFaces.Count + ", candidates " + oracleCandidates
                    + ", roadway " + oracleRoadway.Count + ", non-roadway " + string.Join(";", oracleReasons.Select(pair => pair.Key + "=" + pair.Value))
                    + ", unclassified " + oracleUnclassified);

                // Production envelope must cover exactly the oracle roadway set (count +
                // projected area + identity hash).
                var oracleArea = oracleRoadway.Sum(face =>
                    0.5f * MathF.Abs(((face.B.X - face.A.X) * (face.C.Y - face.A.Y)) - ((face.C.X - face.A.X) * (face.B.Y - face.A.Y))));
                var oracleHash = FaceIdentityHash(oracleRoadway);
                var membershipPlan = bridge.TryPlanClearance(game.TerrainLogic.HeightMap);
                Assert.True(membershipPlan != null, "supported bridge must produce a clearance plan.");
                Assert.Equal(oracleRoadway.Count, membershipPlan.RoadwayFaceCount);
                Assert.Equal(oracleArea, membershipPlan.RoadwayProjectedArea, 0.5f);
                Assert.Equal(oracleHash, membershipPlan.RoadwayFaceIdentityHash);
                Console.WriteLine(
                    "r3-g8 oracle coverage: roadway faces " + oracleRoadway.Count + ", projected area " + oracleArea.ToString("F1", CultureInfo.InvariantCulture)
                    + " sq units, identity hash " + oracleHash[..12] + "..., plan footprint cells " + membershipPlan.FootprintCellCount);

                var heightMap = game.TerrainLogic.HeightMap;
                var footprint = oracleRoadway
                    .Select(face => new[]
                    {
                        new Vector2(face.A.X, face.A.Y),
                        new Vector2(face.B.X, face.B.Y),
                        new Vector2(face.C.X, face.C.Y),
                    })
                    .ToList();

                var borderWidth = (int)game.Scene3D.MapFile.HeightMapData.BorderWidth;
                var lastSampleX = heightMap.Width - 1;
                var lastSampleY = heightMap.Height - 1;
                var lastCellX = heightMap.Width - 2;
                var lastCellY = heightMap.Height - 2;

                // The oracle scans every rendered cell (0..lastCell) — completeness is the
                // point; SAT rejection is cheap.
                var cellMinX = 0;
                var cellMinY = 0;
                var cellMaxX = lastCellX;
                var cellMaxY = lastCellY;

                float overallMin = float.MaxValue;
                var overallArea = 0f;
                var worst = Vector2.Zero;
                var zones = new Dictionary<string, (float Min, double Sum, int Count, float Area)>
                {
                    ["south-bank"] = (float.MaxValue, 0, 0, 0),
                    ["north-bank"] = (float.MaxValue, 0, 0, 0),
                    ["seam-440"] = (float.MaxValue, 0, 0, 0),
                    ["seam-580"] = (float.MaxValue, 0, 0, 0),
                    ["seam-720"] = (float.MaxValue, 0, 0, 0),
                    ["seam-860"] = (float.MaxValue, 0, 0, 0),
                };

                void RecordZone(string zone, float clearance, float area, Vector2 point, bool negative)
                {
                    if (!zones.TryGetValue(zone, out var slot))
                    {
                        return;
                    }

                    slot.Min = MathF.Min(slot.Min, clearance);
                    slot.Sum += clearance;
                    slot.Count++;
                    slot.Area += negative ? area : 0f;
                    zones[zone] = slot;
                }

                foreach (var oracleFace in oracleRoadway)
                {
                    var triangle = new[] { new Vector2(oracleFace.A.X, oracleFace.A.Y), new Vector2(oracleFace.B.X, oracleFace.B.Y), new Vector2(oracleFace.C.X, oracleFace.C.Y) };
                    var triangleMinX = MathF.Min(triangle[0].X, MathF.Min(triangle[1].X, triangle[2].X));
                    var triangleMaxX = MathF.Max(triangle[0].X, MathF.Max(triangle[1].X, triangle[2].X));
                    var triangleMinY = MathF.Min(triangle[0].Y, MathF.Min(triangle[1].Y, triangle[2].Y));
                    var triangleMaxY = MathF.Max(triangle[0].Y, MathF.Max(triangle[1].Y, triangle[2].Y));

                    for (var i = cellMinX; i <= cellMaxX; i++)
                    {
                        for (var j = cellMinY; j <= cellMaxY; j++)
                        {
                            var p00 = heightMap.GetPosition(i, j);
                            var cellMinXy = new Vector2(p00.X, p00.Y);
                            var cellMaxXy = new Vector2(p00.X + HeightMap.HorizontalScale, p00.Y + HeightMap.HorizontalScale);

                            if (cellMaxXy.X < triangleMinX || cellMinXy.X > triangleMaxX || cellMaxXy.Y < triangleMinY || cellMinXy.Y > triangleMaxY)
                            {
                                continue;
                            }

                            var quad = new[]
                            {
                                cellMinXy,
                                new Vector2(cellMaxXy.X, cellMinXy.Y),
                                cellMaxXy,
                                new Vector2(cellMinXy.X, cellMaxXy.Y),
                            };

                            foreach (var cellTriangle in CellTriangles(quad))
                            {
                                var clipped = ClipConvex(triangle, cellTriangle);

                                if (clipped.Count < 3)
                                {
                                    continue;
                                }

                                // Actual roadway Z: barycentric on the oracle roadway face
                                // itself, never a fitted plane.
                                var polygonMin = float.MaxValue;
                                var clearances = new List<float>(clipped.Count);

                                foreach (var point in clipped)
                                {
                                    var clearance = RoadwayZ(oracleFace, point.X, point.Y) - CellSurfaceZ(heightMap, i, j, point.X, point.Y);
                                    polygonMin = MathF.Min(polygonMin, clearance);
                                    clearances.Add(clearance);
                                }

                                var area = ShoelaceArea(clipped);

                                if (polygonMin < overallMin)
                                {
                                    overallMin = polygonMin;
                                    var minimumClearance = clearances.Min();
                                    worst = clipped[clearances.IndexOf(minimumClearance)];
                                }

                                // Zero-contour split: clearance is linear on the clipped
                                // polygon, so its <= 0 region's shoelace area is the exact
                                // interpenetration area.
                                overallArea += ShoelaceArea(SplitAtZeroContour(clipped, clearances));

                                var zoneY = clipped.Average(point => point.Y);
                                var zone = zoneY < 460f
                                    ? "south-bank"
                                    : zoneY >= 840f
                                        ? "north-bank"
                                        : NearestSeamZone(zoneY);

                                if (zone != null)
                                {
                                    RecordZone(zone, polygonMin, area, clipped[0], polygonMin < 0f);
                                }
                            }
                        }
                    }
                }

                Console.WriteLine(
                    "r3-g8 EXACT gate: full-footprint min clearance " + overallMin.ToString("F3", CultureInfo.InvariantCulture) +
                    " at (" + worst.X.ToString("F1", CultureInfo.InvariantCulture) + ", " + worst.Y.ToString("F1", CultureInfo.InvariantCulture) +
                    "); interpenetration area " + overallArea.ToString("F2", CultureInfo.InvariantCulture) + " sq units");

                foreach (var zone in zones)
                {
                    var mean = zone.Value.Count > 0 ? zone.Value.Sum / zone.Value.Count : float.NaN;
                    Console.WriteLine(
                        "r3-g8 zone " + zone.Key + ": min " + zone.Value.Min.ToString("F3", CultureInfo.InvariantCulture) +
                        ", mean " + mean.ToString("F3", CultureInfo.InvariantCulture) +
                        ", samples " + zone.Value.Count +
                        ", negative area " + zone.Value.Area.ToString("F2", CultureInfo.InvariantCulture));
                }

                Assert.True(
                    overallMin >= BridgeSurface.MinimumDeckClearance,
                    $"exact full-width clearance gate: min {overallMin:F3} < {BridgeSurface.MinimumDeckClearance} at ({worst.X:F1}, {worst.Y:F1}); terrain buries the deck.");

                // Dense step-5 min/mean report at both banks and every piece seam, using the
                // same cell-triangle terrain evaluation (report only; the exact gate above is
                // the acceptance bound).
                foreach (var (label, yMin, yMax) in new[]
                         {
                             ("south bank", 300f, 460f),
                             ("north bank", 840f, 1000f),
                             ("seam 440", 435f, 445f),
                             ("seam 580", 575f, 585f),
                             ("seam 720", 715f, 725f),
                             ("seam 860", 855f, 865f),
                         })
                {
                    var samples = new List<float>();
                    var oracleMinX = oracleRoadway.Min(face => face.MinXy.X);
                    var oracleMaxX = oracleRoadway.Max(face => face.MaxXy.X);

                    for (var y = yMin; y <= yMax; y += 5f)
                    {
                        for (var x = MathF.Ceiling(oracleMinX); x <= oracleMaxX; x += 5f)
                        {
                            var samplePoint = new Vector2(x, y);
                            BridgeSurface.BridgeRawFace? containing = null;

                            foreach (var candidateFace in oracleRoadway)
                            {
                                if (PointInTriangle(samplePoint, new[]
                                {
                                    new Vector2(candidateFace.A.X, candidateFace.A.Y),
                                    new Vector2(candidateFace.B.X, candidateFace.B.Y),
                                    new Vector2(candidateFace.C.X, candidateFace.C.Y),
                                }))
                                {
                                    containing = candidateFace;
                                    break;
                                }
                            }

                            if (containing is not { } containingFace)
                            {
                                continue;
                            }

                            var tile = heightMap.GetTilePosition(new Vector2(x, y));

                            if (!tile.HasValue)
                            {
                                continue;
                            }

                            samples.Add(RoadwayZ(containingFace, x, y) - CellSurfaceZ(heightMap, tile.Value.X, tile.Value.Y, x, y));
                        }
                    }

                    if (samples.Count > 0)
                    {
                        Console.WriteLine(
                            "r3-g8 dense " + label + ": n " + samples.Count +
                            ", min " + samples.Min().ToString("F3", CultureInfo.InvariantCulture) +
                            ", mean " + samples.Average().ToString("F3", CultureInfo.InvariantCulture));
                    }
                }

                // Unchanged outside footprint + ring: named lattice samples far from the
                // bridge keep exactly the payload-formula heights (quantized to the resident
                // grid the converter stores). BorderWidth is a lattice count.
                foreach (var (latticeX, latticeY) in new[] { (35, 30), (75, 30), (35, 100), (75, 100), (55, 10), (55, 115), (20, 64) })
                {
                    var expected = ResidentGridHeight(PayloadHeight(latticeX, latticeY));
                    var actual = heightMap.GetHeight(latticeX + (int)borderWidth, latticeY + (int)borderWidth);
                    Assert.True(
                        MathF.Abs(actual - expected) <= 0.04f,
                        $"lattice ({latticeX}, {latticeY}) outside footprint+ring changed: {actual:F4} vs payload {expected:F4}.");
                }

                // Reload determinism: load the same package twice; the cut is a pure function
                // of (package heights, bridge geometry), so the final lattice in the cut
                // region must be identical (no double-cut drift).
                var snapshot = new Dictionary<(int X, int Y), float>();

                for (var i = cellMinX; i <= cellMaxX + 1; i++)
                {
                    for (var j = cellMinY; j <= cellMaxY + 1; j++)
                    {
                        snapshot[(i, j)] = heightMap.GetHeight(i, j);
                    }
                }

                game.LoadMap(package, [], GameType.SinglePlayer);
                var reloaded = game.TerrainLogic.HeightMap;

                foreach (var pair in snapshot)
                {
                    Assert.Equal(pair.Value, reloaded.GetHeight(pair.Key.X, pair.Key.Y));
                }

                // The runtime must not write the converted package: bytes are identical after
                // two full game loads.
                Assert.Equal(packageHashBefore, HashDirectory(package));

            }
            finally
            {
                Directory.Delete(scratchRoot, recursive: true);
            }
        }
        finally
        {
            Panopticon.Terrain.Bridge.GeneratedPathPlanEnabled = true;
        }
    }

    /// <summary>Actual roadway Z at the XY point: barycentric interpolation on the
    /// oracle face's own world triangle (winding-agnostic), NaN when outside.</summary>
    private static float RoadwayZ(BridgeSurface.BridgeRawFace face, float x, float y)
    {
        var denominator = ((face.B.Y - face.C.Y) * (face.A.X - face.C.X)) + ((face.C.X - face.B.X) * (face.A.Y - face.C.Y));

        if (denominator == 0f || !float.IsFinite(denominator))
        {
            return float.NaN;
        }

        var weightA = (((face.B.Y - face.C.Y) * (x - face.C.X)) + ((face.C.X - face.B.X) * (y - face.C.Y))) / denominator;
        var weightB = (((face.C.Y - face.A.Y) * (x - face.C.X)) + ((face.A.X - face.C.X) * (y - face.C.Y))) / denominator;
        var weightC = 1f - weightA - weightB;

        if (weightA < -0.001f || weightB < -0.001f || weightC < -0.001f)
        {
            return float.NaN;
        }

        return (face.A.Z * weightA) + (face.B.Z * weightB) + (face.C.Z * weightC);
    }

    /// <summary>
    /// Splits a polygon (with per-vertex linear clearance values) at the zero contour and
    /// returns the &le;0 region: the exact rendered-area where terrain buries the face.
    /// </summary>
    private static List<Vector2> SplitAtZeroContour(List<Vector2> polygon, List<float> clearances)
    {
        var negative = new List<Vector2>();

        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Count];
            var currentClearance = clearances[index];
            var nextClearance = clearances[(index + 1) % clearances.Count];

            if (currentClearance <= 0f)
            {
                negative.Add(current);
            }

            if ((currentClearance < 0f && nextClearance > 0f) || (currentClearance > 0f && nextClearance < 0f))
            {
                var t = currentClearance / (currentClearance - nextClearance);
                negative.Add(current + ((next - current) * t));
            }
        }

        return negative;
    }

    /// <summary>Canonical roadway-membership identity hash: SHA-256 over the sorted
    /// piece|copy|source-face identity strings (production plan must match exactly).</summary>
    private static string FaceIdentityHash(IReadOnlyList<BridgeSurface.BridgeRawFace> faces)
    {
        var identities = faces.Select(face => $"{face.PieceName}|{face.CopyIndex}|{face.SourceFaceIndex}").OrderBy(identity => identity, StringComparer.Ordinal).ToList();

        using var sha = System.Security.Cryptography.SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join(";", identities))));
    }

    /// <summary>The two rendered triangles of one terrain cell: the patch anti-diagonal
    /// runs from (x+1,y) to (x,y+1), matching TerrainPatch's vertex layout.</summary>
    private static List<Vector2[]> CellTriangles(Vector2[] quad)
    {
        return [
            [quad[0], quad[1], quad[3]],
            [quad[1], quad[2], quad[3]],
        ];
    }

    /// <summary>Rendered terrain height at a point inside lattice cell (i, j): evaluates
    /// the cell sub-triangle the anti-diagonal (x+1,y)-(x,y+1) puts the point in, i.e. the
    /// exact piecewise-linear rendered surface, not the bilinear lattice average.</summary>
    private static float CellSurfaceZ(HeightMap heightMap, int i, int j, float x, float y)
    {
        var p00 = heightMap.GetPosition(i, j);
        var p10 = heightMap.GetPosition(i + 1, j);
        var p01 = heightMap.GetPosition(i, j + 1);
        var p11 = heightMap.GetPosition(i + 1, j + 1);
        var h00 = heightMap.GetHeight(i, j);
        var h10 = heightMap.GetHeight(i + 1, j);
        var h01 = heightMap.GetHeight(i, j + 1);
        var h11 = heightMap.GetHeight(i + 1, j + 1);

        var u = (x - p00.X) / HeightMap.HorizontalScale;
        var v = (y - p00.Y) / HeightMap.HorizontalScale;
        var antiDiagonalSum = 1f; // (x0 + y0 + scale) in cell units: u + v == 1 on the diagonal

        if (u + v <= antiDiagonalSum)
        {
            return h00 + ((h10 - h00) * u) + ((h01 - h00) * v);
        }

        return h11 + ((h10 - h11) * (1f - u)) + ((h01 - h11) * (1f - v));
    }

    /// <summary>Sutherland–Hodgman convex clip: subject polygon against the clip
    /// triangle's three half-planes (clip triangles are authored counter-clockwise).</summary>
    private static List<Vector2> ClipConvex(Vector2[] subject, Vector2[] clip)
    {
        var output = new List<Vector2>(subject);

        for (var edge = 0; edge < clip.Length && output.Count > 0; edge++)
        {
            var a = clip[edge];
            var b = clip[(edge + 1) % clip.Length];

            float Side(Vector2 p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

            var input = output;
            output = [];

            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index];
                var next = input[(index + 1) % input.Count];
                var currentIn = Side(current) >= 0f;
                var nextIn = Side(next) >= 0f;

                if (currentIn)
                {
                    output.Add(current);
                }

                if (currentIn != nextIn)
                {
                    var edgeVector = next - current;
                    var denominator = ((b.X - a.X) * edgeVector.Y) - ((b.Y - a.Y) * edgeVector.X);

                    if (MathF.Abs(denominator) > 1e-9f)
                    {
                        // Solve cross(b-a, current + t*edge - a) = 0.
                        var numerator = ((b.X - a.X) * (current.Y - a.Y)) - ((b.Y - a.Y) * (current.X - a.X));
                        var t = -numerator / denominator;
                        output.Add(current + (edgeVector * t));
                    }
                }
            }
        }

        return output;
    }

    private static float ShoelaceArea(List<Vector2> polygon)
    {
        var sum = 0f;

        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return MathF.Abs(sum) * 0.5f;
    }

    private static bool PointInTriangle(Vector2 p, Vector2[] triangle)
    {
        float Cross(Vector2 a, Vector2 b) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
        var d1 = Cross(triangle[0], triangle[1]);
        var d2 = Cross(triangle[1], triangle[2]);
        var d3 = Cross(triangle[2], triangle[0]);
        var hasNegative = (d1 < -1e-6f) || (d2 < -1e-6f) || (d3 < -1e-6f);
        var hasPositive = (d1 > 1e-6f) || (d2 > 1e-6f) || (d3 > 1e-6f);

        return !(hasNegative && hasPositive);
    }

    private static string? NearestSeamZone(float y)
    {
        foreach (var seam in new[] { 440f, 580f, 720f, 860f })
        {
            if (MathF.Abs(y - seam) <= 5f)
            {
                return "seam-" + seam.ToString("F0", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string HashDirectory(string directory)
    {
        using var sha = SHA256.Create();

        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return System.Convert.ToHexString(sha.Hash!);
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerOrder_CrossesBridgeOnDeck_WithoutGorgeDip()
    {
        // Kept after the run for capture evidence; never deleted here.
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            var packageOff = Path.Combine(scratchRoot, "package-off");

            Convert(payloadDir, packageOn, withRules: true);
            Convert(payloadDir, packageOff, withRules: false);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            // ---- OFF (before behavior; documented, not asserted): raw terrain drives the
            // ---- dozer into the gorge. G7 witness: dozer Z 1020 vs deck 1109 at mid-span.
            game.LoadMap(packageOff, [], GameType.SinglePlayer);
            {
                Assert.Empty(game.Scene3D.Bridges);
                Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, 650f));

                var beforeTrace = RunDozerTraversal(game, (550f, 150f), (550f, 1100f));
                var beforeMid = beforeTrace.Where(sample => sample.Y > GorgeMinY && sample.Y < GorgeMaxY).ToList();

                if (beforeMid.Count > 0)
                {
                    var lowest = beforeMid.Min(sample => sample.Z);
                    Console.WriteLine(
                        $"r3-g8 BEFORE (no bridge surface): gorge-band samples {beforeMid.Count}, lowest dozer Z {lowest.ToString("F1", CultureInfo.InvariantCulture)} " +
                        $"(deck approx 1090; the dozer dips {lowest < 1070f}); max |dz| per frame " +
                        $"{MaxZStep(beforeTrace).ToString("F2", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    Console.WriteLine("r3-g8 BEFORE: dozer did not enter the gorge band with raw terrain heights.");
                }
            }

            // ---- ON (after): the bridge surface carries the vehicle across on the deck.
            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            {
                Assert.NotEmpty(game.Scene3D.Bridges);

                // Print the deck profile and bridge facts for the evidence report before
                // any assertion, so a surface-coverage gap shows its shape directly.
                var bridge = game.Scene3D.Bridges[0];
                Console.WriteLine(
                    "r3-g8 bridge facts: bridges " + game.Scene3D.Bridges.Length +
                    ", anchor start " + bridge.StartPositionWithHeight.ToString("F2", CultureInfo.InvariantCulture) +
                    ", anchor end " + bridge.EndPositionWithHeight.ToString("F2", CultureInfo.InvariantCulture) +
                    ", surface triangles " + (bridge.CreateSurface()?.TriangleCount.ToString(CultureInfo.InvariantCulture) ?? "0") +
                    ", intrinsic grade rad " + (bridge.IntrinsicGradeRadians?.ToString("F4", CultureInfo.InvariantCulture) ?? "none"));

                // The scoped pitch correction must have measured the authored roadway pitch
                // (WP1029 rework part 2): on this fixture it is the site pitch the axis
                // alignment embeds, atan(35/700) ≈ 0.05 rad.
                Assert.True(bridge.IntrinsicGradeRadians.HasValue, "the intrinsic roadway grade must be measured for generated bridges.");
                Assert.InRange(bridge.IntrinsicGradeRadians.Value, 0.03f, 0.08f);

                for (var y = 240f; y <= 1060f; y += 20f)
                {
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(550f, y);
                    var deckZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, y);
                    Console.WriteLine(
                        $"r3-g8 profile y={y.ToString("F0", CultureInfo.InvariantCulture)} terrain {terrainZ.ToString("F2", CultureInfo.InvariantCulture)} deck {(deckZ?.ToString("F2", CultureInfo.InvariantCulture) ?? "none")}");
                }

                // Landing gap tables (WP1029 rework part 2): post-correction, deck surface
                // minus graded terrain must stay within ±5 at both landings across the
                // cross-width. The placement pitch correction flattens the deck to the
                // anchor line, so these replace the old +19/+35 exit drops.
                var independent = bridge.CreateSurface();
                Assert.NotNull(independent);

                foreach (var (label, ys) in new[]
                         {
                             ("south", new[] { 305f, 310f, 315f, 320f }),
                             ("north", new[] { 980f, 985f, 990f, 995f }),
                         })
                {
                    foreach (var y in ys)
                    {
                        foreach (var x in new[] { 530f, 550f, 570f })
                        {
                            var deckZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(x, y);
                            var gradedZ = game.TerrainLogic.HeightMap.GetHeight(x, y);
                            Assert.True(deckZ.HasValue, $"no deck surface at ({x:F0}, {y:F0}) on the {label} landing.");
                            var gap = deckZ.Value - gradedZ;
                            Console.WriteLine(
                                $"r3-g8 landing {label} ({x:F0},{y:F0}): deck {deckZ.Value.ToString("F2", CultureInfo.InvariantCulture)} graded {gradedZ.ToString("F2", CultureInfo.InvariantCulture)} gap {gap.ToString("F2", CultureInfo.InvariantCulture)}");
                        }
                    }
                }

                // Piece seams: the roadway must not step at the landing→span or span→span
                // piece boundaries.
                foreach (var seamY in new[] { 440f, 580f, 720f, 860f })
                {
                    var before = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, seamY - 3f);
                    var after = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, seamY + 3f);
                    Assert.True(before.HasValue && after.HasValue, $"no deck surface across the piece seam at y≈{seamY:F0}.");
                    Assert.True(
                        MathF.Abs(after.Value - before.Value) <= 3f,
                        $"piece seam step {after.Value - before.Value:F2} at y≈{seamY:F0} (before {before.Value:F2}, after {after.Value:F2}) exceeds 3.");
                }

                // The bridge registered its roadway surface: mid-span queries resolve high
                // above the gorge floor (terrain 920.75 there, deck around 1090).
                var midSpanSurfaceZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, 650f);
                Assert.True(midSpanSurfaceZ.HasValue, "the bridge surface must cover the mid-span XY.");
                Assert.True(midSpanSurfaceZ.Value > 1050f, $"mid-span surface Z {midSpanSurfaceZ.Value} must sit near deck height, not gorge terrain.");

                var trace = RunDozerTraversal(game, (550f, 150f), (550f, 1100f));

                // The dozer crossed the whole span.
                var maxY = trace.Max(sample => sample.Y);
                Assert.True(maxY > 1060f, $"the dozer reached only y={maxY}; it must cross the bridge toward (550, 1100).");

                var gorgeSamples = trace.Where(sample => sample.Y > GorgeMinY && sample.Y < GorgeMaxY).ToList();
                Assert.True(gorgeSamples.Count > 0, "the dozer never entered the gorge band; it must drive across the span.");

                // On-deck: every gorge-band sample rides the bridge surface within ±5.
                foreach (var sample in gorgeSamples)
                {
                    Assert.True(sample.SurfaceZ.HasValue, $"no bridge surface at ({sample.X:F1}, {sample.Y:F1}) mid-span.");
                    var delta = sample.Z - sample.SurfaceZ.Value;
                    Assert.True(
                        MathF.Abs(delta) <= 5f,
                        $"dozer Z {sample.Z:F2} vs deck {sample.SurfaceZ.Value:F2} at y={sample.Y:F1} (frame {sample.Frame}); the vehicle must stay on the deck within ±5.");
                }

                var lowestOnDeck = gorgeSamples.Min(sample => sample.Z);
                var gorgeTerrainZ = game.TerrainLogic.HeightMap.GetHeight(550f, 640f);
                Assert.True(
                    lowestOnDeck > gorgeTerrainZ + 50f,
                    $"dozer lowest gorge Z {lowestOnDeck:F1} against gorge terrain {gorgeTerrainZ:F1}; the vehicle dipped into the gorge.");

                // No Z discontinuity above 3 units per logic frame across the whole run.
                var maxStep = MaxZStep(trace);
                Assert.True(maxStep <= 3f, $"max per-frame Z step {maxStep:F2} exceeds the 3-unit discontinuity bound.");

                // Contact clearance mid-span: Translation.Z - surface Z within [-1, +3].
                var contact = gorgeSamples.Select(sample => sample.Z - sample.SurfaceZ!.Value).ToList();
                Assert.True(
                    contact.All(delta => delta is >= -1f and <= 3f),
                    $"contact clearance mid-span outside [-1, +3]: [{contact.Min():F2}, {contact.Max():F2}].");

                // Exit transition: post-correction the deck parallels the anchor line and
                // ends ≈1.5 units above the graded north landing ramp — the vehicle steps
                // down onto graded terrain instead of the previous 19/35-unit drop.
                var exitSamples = trace.Where(sample => sample.Y > 1040f && sample.Y < 1090f).ToList();
                Assert.True(exitSamples.Count > 0, "the dozer never reached the north landing.");
                foreach (var sample in exitSamples)
                {
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(sample.X, sample.Y);
                    Assert.True(
                        MathF.Abs(sample.Z - terrainZ) <= 8f,
                        $"exit Z {sample.Z:F2} vs terrain {terrainZ:F2} at y={sample.Y:F1}; the vehicle must return to (graded) terrain.");
                }

                // Independent contact (WP1029 rework): compare the vehicle against a FRESH
                // BridgeSurface instance (CreateSurface) rather than the registered list the
                // locomotor consulted — same triangulation, no shared query state.
                foreach (var sample in gorgeSamples)
                {
                    var independentZ = independent!.GetSurfaceZ(sample.X, sample.Y);
                    Assert.True(independentZ.HasValue, $"independent surface missing at ({sample.X:F1}, {sample.Y:F1}).");
                    Assert.True(
                        MathF.Abs(sample.Z - independentZ.Value) <= 5f,
                        $"independent contact: dozer {sample.Z:F2} vs reconstructed deck {independentZ.Value:F2} at y={sample.Y:F1}.");
                }

                Console.WriteLine(
                    $"r3-g8 AFTER (south→north): frames {trace.Count}, gorge samples {gorgeSamples.Count}, max |dz|/frame {maxStep:F2}, " +
                    $"contact clearance [{contact.Min():F2}, {contact.Max():F2}], final y {maxY:F1}.");

                // Lateral surface samples (r3-g8 corrective wording: these are ±15-unit
                // lateral surface probes along the gorge band, NOT wheel landmarks; the
                // vehicle geometry radii are body boxes, not wheels): the deck surface
                // sampled at ±15 lateral must match the vehicle Z within ±5.
                foreach (var sample in gorgeSamples)
                {
                    foreach (var offset in new[] { -15f, 15f })
                    {
                        var lateralX = sample.X + offset;

                        if (lateralX < independent!.ExtentMin.X || lateralX > independent.ExtentMax.X)
                        {
                            continue;
                        }

                        var lateralZ = independent.GetSurfaceZ(lateralX, sample.Y);
                        Assert.True(lateralZ.HasValue, $"no deck surface at lateral sample ({lateralX:F1}, {sample.Y:F1}).");
                        Assert.True(
                            MathF.Abs(sample.Z - lateralZ.Value) <= 5f,
                            $"lateral surface sample: vehicle Z {sample.Z:F2} vs deck {lateralZ.Value:F2} at ({lateralX:F1}, {sample.Y:F1}).");
                    }
                }

                // ---- Bidirectional proof: north→south on the same bridge. A second dozer
                // starts at the north end and drives to the south landing.
                var player2 = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
                var dozer2Definition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
                var dozer2 = game.Scene3D.GameObjects.CreateObject(dozer2Definition!, player2);
                var northZ = game.TerrainLogic.HeightMap.GetHeight(550f, 1100f);
                dozer2.SetTranslation(new Vector3(550f, 1100f, northZ));

                player2.SelectUnits([dozer2]);
                var order2 = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player2), new Vector3(550f, 150f, 0f));
                new OrderProcessor(game).Process([order2]);

                var traceReverse = new List<TraceSample>(4096);

                for (var frame = 0; frame < 2400; frame++)
                {
                    game.Update(Array.Empty<InputMessage>());

                    var position = dozer2.Translation;
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
                    var surfaceZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y);
                    traceReverse.Add(new TraceSample(frame, position.X, position.Y, position.Z, terrainZ, surfaceZ));

                    if (position.Y <= 152f)
                    {
                        break;
                    }
                }

                var reverseMaxYDistance = traceReverse.Min(sample => sample.Y);
                Assert.True(reverseMaxYDistance < 340f, $"the reverse dozer only reached y={reverseMaxYDistance:F1}; it must cross back toward (550, 150).");

                var reverseGorge = traceReverse.Where(sample => sample.Y > GorgeMinY && sample.Y < GorgeMaxY).ToList();
                Assert.True(reverseGorge.Count > 0, "the reverse dozer never entered the gorge band.");
                foreach (var sample in reverseGorge)
                {
                    Assert.True(sample.SurfaceZ.HasValue, $"no bridge surface under the reverse dozer at ({sample.X:F1}, {sample.Y:F1}).");
                    Assert.True(
                        MathF.Abs(sample.Z - sample.SurfaceZ.Value) <= 5f,
                        $"reverse dozer Z {sample.Z:F2} vs deck {sample.SurfaceZ.Value:F2} at y={sample.Y:F1}.");
                }

                var reverseStep = MaxZStep(traceReverse);
                Assert.True(reverseStep <= 3f, $"reverse max per-frame Z step {reverseStep:F2} exceeds the 3-unit discontinuity bound.");

                var reverseIndependent = traceReverse.Select(sample => (sample, deckZ: independent!.GetSurfaceZ(sample.X, sample.Y))).ToList();
                foreach (var (sample, deckZ) in reverseIndependent.Where(pair => pair.sample.SurfaceZ.HasValue))
                {
                    Assert.True(deckZ.HasValue, $"independent surface missing at ({sample.X:F1}, {sample.Y:F1}) reverse.");
                    Assert.True(
                        MathF.Abs(sample.Z - deckZ.Value) <= 5f,
                        $"reverse independent contact: dozer {sample.Z:F2} vs deck {deckZ.Value:F2} at y={sample.Y:F1}.");
                }

                Console.WriteLine(
                    $"r3-g8 AFTER (north→south): frames {traceReverse.Count}, gorge samples {reverseGorge.Count}, max |dz|/frame {reverseStep:F2}, " +
                    $"contact [{reverseGorge.Min(s => s.Z - s.SurfaceZ!.Value):F2}, {reverseGorge.Max(s => s.Z - s.SurfaceZ!.Value):F2}].");
            }
        }
        finally
        {
            // Scratch root intentionally preserved for capture evidence.
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerCrossing_CaptureTwoAngles_TraceAndGifFrames()
    {
        // Kept after the run for evidence preservation (the capture PNGs); never deleted here.
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");

            Convert(payloadDir, packageOn, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            // Captures always land in a portable per-run directory; the operator capture
            // root (/mnt/pit2, host-local only) receives best-effort additional copies
            // when it is mounted, so the fact runs unchanged in the MCP container.
            var portableRoot = Path.Combine(Path.GetTempPath(), "r3-g8-traversal", Guid.NewGuid().ToString("N"));
            var framesRoot = Path.Combine(portableRoot, "frames", "r3-g8-bridge-traversal-v2");
            Directory.CreateDirectory(framesRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            Assert.NotEmpty(game.Scene3D.Bridges);

            foreach (var angleName in new[] { "side", "oblique" })
            {
                var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
                player.DisableAI();

                var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
                Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");

                var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
                var startZ = game.TerrainLogic.GetGroundHeight(550f, 150f);
                dozer.SetTranslation(new Vector3(550f, 150f, startZ));

                game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();

                // Direct camera placement (Camera.SetLookAt after each logic tick, which
                // TacticalView would otherwise overwrite): two static 3/4 views framing the
                // full span. The tactical camera path is unusable here because its
                // ground-level clamp bakes a Z≈120 map model into the offset math, which on
                // this Z≈1000 map parks the eye outside the map or in the terrain void.
                var eye = angleName == "side"
                    ? new Vector3(1400f, 650f, 1620f)
                    : new Vector3(0f, 40f, 1680f);
                var lookTarget = angleName == "side"
                    ? new Vector3(550f, 650f, 1110f)
                    : new Vector3(600f, 640f, 1115f);

                game.StartRun();
                game.EnableFixedLogicTimestep();

                while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
                {
                    game.Scene2D.WndWindowManager.PopWindow();
                }

                // Settle window: hold the camera so terrain streams in around the eye
                // before the dozer starts crossing.
                for (var settle = 0; settle < 30; settle++)
                {
                    game.Scene3D.Camera.SetLookAt(eye, lookTarget, Vector3.UnitZ);
                    game.Update(Array.Empty<InputMessage>());
                }

                player.SelectUnits([dozer]);
                var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), new Vector3(550f, 1100f, 0f));
                new OrderProcessor(game).Process([order]);
                var tracePath = Path.Combine(framesRoot, $"position-trace-{angleName}-v2.csv");
                var cameraPath = Path.Combine(framesRoot, $"camera-{angleName}-v2.txt");
                var traceLines = new List<string> { "frame,x,y,z,terrain_z,surface_z" };

                const float captureStepTransition = 20f;
                const float captureStepMidspan = 60f;
                var nextCaptureY = 160f;
                var captured = 0;

                for (var frame = 0; frame < MaxCaptureFrames; frame++)
                {
                    game.Update(Array.Empty<InputMessage>());

                    var position = dozer.Translation;
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
                    var surfaceZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y);
                    traceLines.Add(string.Create(CultureInfo.InvariantCulture, $"{frame},{position.X:F2},{position.Y:F2},{position.Z:F2},{terrainZ:F2},{(surfaceZ?.ToString("F2") ?? string.Empty)}"));

                    if (position.Y >= nextCaptureY && captured < MaxCaptureCount)
                    {
                        game.Scene3D.Camera.SetLookAt(eye, lookTarget, Vector3.UnitZ);
                        game.Panel.EnsureFrame(new Rectangle(0, 0, CaptureWidth, CaptureHeight));
                        game.Render();

                        var pngPath = Path.Combine(framesRoot, $"{angleName}-{captured:00}-f{frame:0000}.png");
                        FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, pngPath);
                        captured++;

                        var step = nextCaptureY < 340f || nextCaptureY > 960f
                            ? captureStepTransition
                            : captureStepMidspan;
                        nextCaptureY += step;
                    }

                    if (position.Y >= 1098f)
                    {
                        break;
                    }
                }

                File.WriteAllLines(tracePath, traceLines);
                File.WriteAllText(
                    cameraPath,
                    $"angle-name={angleName}\neye={eye.X.ToString("F1", CultureInfo.InvariantCulture)},{eye.Y.ToString("F1", CultureInfo.InvariantCulture)},{eye.Z.ToString("F1", CultureInfo.InvariantCulture)}\n" +
                    $"look-at={lookTarget.X.ToString("F1", CultureInfo.InvariantCulture)},{lookTarget.Y.ToString("F1", CultureInfo.InvariantCulture)},{lookTarget.Z.ToString("F1", CultureInfo.InvariantCulture)}\n" +
                    $"camera-mode=static Camera.SetLookAt per captured frame (tactical view overwritten by game.Update)\n" +
                    $"resolution={CaptureWidth}x{CaptureHeight}\n" +
                    $"capture-cadence=every 5 logic frames in landing bands (y<340, y>960), else every 15\nframes-captured={captured}\n" +
                    $"dozer-definition={DozerDefinitionName}\norder=(550,150)->(550,1100)\n");
                Console.WriteLine($"r3-g8 capture {angleName}: {captured} frames, trace {traceLines.Count - 1} samples -> {framesRoot}");
            }

            // The GIF deliverable is assembled from the portable frames so the crossing
            // evidence is complete without the operator capture root; each GIF is verified
            // against the captured frame count (20-30 frames at 4 fps).
            var gifPaths = new List<string>();

            foreach (var angleName in new[] { "side", "oblique" })
            {
                var gifPath = BuildTraversalGif(framesRoot, portableRoot, angleName);
                gifPaths.Add(gifPath);
            }

            PublishCaptureRoot(portableRoot, gifPaths);
        }
        finally
        {
            // Scratch and portable roots intentionally preserved for capture evidence.
        }
    }

    /// <summary>
    /// Assembles the capture frames for one angle into the portable GIF (4 fps, 250 ms per
    /// frame) and returns the GIF path; the frame count must match the capture set and
    /// stay within the 20-30 frame band the evidence contract requires.
    /// </summary>
    private static string BuildTraversalGif(string framesRoot, string portableRoot, string angleName)
    {
        var pngPaths = Directory.GetFiles(framesRoot, $"{angleName}-*.png").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.True(
            pngPaths.Length is >= 20 and <= 30,
            $"{angleName}: {pngPaths.Length} captured frames; the evidence contract needs 20-30.");

        using var master = Image.Load(pngPaths[0]);

        for (var index = 1; index < pngPaths.Length; index++)
        {
            using var next = Image.Load(pngPaths[index]);
            master.Frames.AddFrame(next.Frames.RootFrame);
        }

        foreach (var frame in master.Frames)
        {
            frame.Metadata.GetGifMetadata().FrameDelay = GifFrameDelayCentiseconds;
        }

        var gifPath = Path.Combine(portableRoot, $"r3-g8-bridge-traversal-{angleName}-v2.gif");
        master.SaveAsGif(gifPath);

        using var verify = Image.Load(gifPath);
        Assert.Equal(pngPaths.Length, verify.Frames.Count);

        Console.WriteLine($"r3-g8 gif {angleName}: {verify.Frames.Count} frames @ 4 fps -> {gifPath} ({new FileInfo(gifPath).Length} bytes)");
        return gifPath;
    }

    /// <summary>
    /// Best-effort publication of the portable capture set to the operator capture root:
    /// GIFs top-level and the frames directory beneath it. Existing published files are
    /// never overwritten (the historical copy stays); without the mount this is skipped.
    /// </summary>
    private static void PublishCaptureRoot(string portableRoot, IReadOnlyList<string> gifPaths)
    {
        if (!Directory.Exists(ExternalCaptureRoot))
        {
            Console.WriteLine($"external capture root not mounted, skipped: {ExternalCaptureRoot}");
            return;
        }

        foreach (var gifPath in gifPaths)
        {
            PublishFile(gifPath, ExternalCaptureRoot, Path.GetFileName(gifPath));
        }

        var portableFrames = Path.Combine(portableRoot, "frames", "r3-g8-bridge-traversal-v2");
        var externalFrames = Path.Combine(ExternalCaptureRoot, "frames", "r3-g8-bridge-traversal-v2");
        Directory.CreateDirectory(externalFrames);

        foreach (var file in Directory.GetFiles(portableFrames))
        {
            PublishFile(file, externalFrames, Path.GetFileName(file));
        }
    }

    private static void PublishFile(string sourcePath, string directory, string publishedName)
    {
        var publishedPath = Path.Combine(directory, publishedName);

        if (File.Exists(publishedPath))
        {
            // Never overwrite published evidence; the existing file is the historical copy.
            Console.WriteLine($"published capture already exists, kept: {publishedPath}");
            return;
        }

        File.Copy(sourcePath, publishedPath);
        Console.WriteLine($"published capture: {publishedPath} ({new FileInfo(publishedPath).Length} bytes)");
    }

    [GameFact(SageGame.CncGenerals)]
    public void LegacyMap_WithBridges_RegistersNoSurfaces_VehicleFollowsRawTerrain()
    {
        // WP1029 rework: legacy maps keep raw ground behavior. The generated-provenance
        // property is the only registration trigger, so a legacy map's bridges register
        // no roadway surfaces and the dedicated movement query answers null everywhere.
        var candidates = new[]
        {
            "maps/CHI06/CHI06.map",
            "maps/GLA06/GLA06.map",
            "maps/Golden Oasis/Golden Oasis.map",
            "maps/USA01/USA01.map",
            "maps/CHI01/CHI01.map",
            "maps/Armored Fury/Armored Fury.map",
            "maps/Heartland Shield/Heartland Shield.map",
        };

        var userRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-legacy-bridges", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userRoot);

        using var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
            window: null);

        var loaded = false;
        string? mapPath = null;

        foreach (var candidate in candidates)
        {
            if (game.ContentManager.GetMapEntry(candidate) is null)
            {
                continue;
            }

            game.LoadMap(candidate, [], GameType.SinglePlayer);

            if (game.Scene3D.Bridges.Length > 0)
            {
                loaded = true;
                mapPath = candidate;
                break;
            }
        }

        Assert.True(loaded, $"no candidate legacy map carries bridge pairs; loaded maps: {string.Join(", ", candidates)}");
        Console.WriteLine($"r3-g8 legacy map with bridges: {mapPath}, bridges {game.Scene3D.Bridges.Length}");

        // No generated provenance on any legacy bridge; no roadway surface registered.
        Assert.All(game.Scene3D.Bridges, bridge => Assert.False(bridge.IsGeneratedBridge));
        foreach (var bridge in game.Scene3D.Bridges)
        {
            var midX = (bridge.StartPositionWithHeight.X + bridge.EndPositionWithHeight.X) * 0.5f;
            var midY = (bridge.StartPositionWithHeight.Y + bridge.EndPositionWithHeight.Y) * 0.5f;
            Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(midX, midY));
            Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(bridge.StartPositionWithHeight.X, bridge.StartPositionWithHeight.Y));
        }

        // A vehicle near the first bridge keeps raw terrain height (no deck override).
        var firstBridge = game.Scene3D.Bridges[0];
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
        Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");
        var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);

        var anchorX = firstBridge.StartPositionWithHeight.X;
        var anchorY = firstBridge.StartPositionWithHeight.Y;
        dozer.SetTranslation(new Vector3(anchorX, anchorY, game.TerrainLogic.HeightMap.GetHeight(anchorX, anchorY)));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        for (var frame = 0; frame < 20; frame++)
        {
            game.Update(Array.Empty<InputMessage>());
        }

        var position = dozer.Translation;
        var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
        Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y));
        Assert.True(
            MathF.Abs(position.Z - terrainZ) <= 5f,
            $"legacy-map vehicle Z {position.Z:F2} vs raw terrain {terrainZ:F2} at ({position.X:F1}, {position.Y:F1}); legacy behavior changed.");
        Console.WriteLine(
            $"r3-g8 legacy vehicle near bridge: Z {position.Z.ToString("F2", CultureInfo.InvariantCulture)} vs terrain {terrainZ.ToString("F2", CultureInfo.InvariantCulture)} (raw behavior kept)");
    }

    private static List<TraceSample> RunDozerTraversal(Game game, (float X, float Y) start, (float X, float Y) goal)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
        Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");

        var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
        Assert.NotNull(dozer);

        var startZ = game.TerrainLogic.GetGroundHeight(start.X, start.Y);
        dozer.SetTranslation(new Vector3(start.X, start.Y, startZ));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        player.SelectUnits([dozer]);

        var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), new Vector3(goal.X, goal.Y, 0f));
        new OrderProcessor(game).Process([order]);

        var trace = new List<TraceSample>(4096);
        const int maxFrames = 6000;

        for (var frame = 0; frame < maxFrames; frame++)
        {
            game.Update(Array.Empty<InputMessage>());

            var position = dozer.Translation;
            var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
            var surfaceZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y);
            trace.Add(new TraceSample(frame, position.X, position.Y, position.Z, terrainZ, surfaceZ));

            if (position.Y >= goal.Y - 2f)
            {
                break;
            }
        }

        return trace;
    }

    private static float MaxZStep(List<TraceSample> trace)
    {
        var max = 0f;

        for (var index = 1; index < trace.Count; index++)
        {
            max = MathF.Max(max, MathF.Abs(trace[index].Z - trace[index - 1].Z));
        }

        return max;
    }

    private static double Graded(double current, double deckZ, double along)
    {
        var rampT = 1d - (along / 40d);
        var weight = (rampT * rampT) * (3d - (2d * rampT));
        var target = deckZ - 1d;
        var graded = current + ((target - current) * weight);

        return Math.Clamp(graded, Math.Min(current, deckZ), Math.Max(current, deckZ));
    }

    private static float ResidentGridHeight(double value)
    {
        const double step = 0.0390625d;
        return (float)(Math.Round(value / step, MidpointRounding.ToEven) * step);
    }

    private static float ReloadedLatticeHeight(MapInterchange model, int latticeWidth, int latticeX, int latticeY)
    {
        var cx = latticeX / ChunkLayout.IntervalsPerChunk;
        var cy = latticeY / ChunkLayout.IntervalsPerChunk;
        var chunk = Assert.Single(model.Layout.Chunks, candidate => candidate.Key.Cx == cx && candidate.Key.Cy == cy);
        var originX = cx * ChunkLayout.IntervalsPerChunk;
        var originY = cy * ChunkLayout.IntervalsPerChunk;
        var coreWidth = Math.Min(ChunkLayout.PointsPerChunkAxis, model.Layout.LatticeWidth - originX);

        return chunk.Heights.Samples[((latticeY - originY + 1) * (coreWidth + 2)) + (latticeX - originX + 1)];
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

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(BridgeEndpoints[0].X, BridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(BridgeEndpoints[1].X, BridgeEndpoints[1].Y))]),
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

    private static void Convert(string payloadDir, string packageDir, bool withRules, string bridgeTemplate = "Concrete")
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
            "r3-g8-bridge-traversal",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors", bridgeTemplate)] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-bridge-traversal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private readonly record struct TraceSample(int Frame, float X, float Y, float Z, float TerrainZ, float? SurfaceZ);

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
