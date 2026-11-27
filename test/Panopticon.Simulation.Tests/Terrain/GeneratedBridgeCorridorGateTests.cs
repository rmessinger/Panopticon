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
/// Geometric gates for the corridor plan on the standard fixture, checked against the terrain as
/// the loader leaves it: the marked package takes the level path at load, so the test reads
/// LevelLayout and CorridorPlan off the loaded bridge instead of re-planning and re-applying.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedBridgeCorridorGateTests
{
    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_LoadedCorridor_PassesTheGeometricGates()
    {
        var pristine = SnapshotPristine();
        WithLoadedGame(game =>
        {
            var heightMap = game.TerrainLogic.HeightMap;
            Assert.NotNull(heightMap);
            var map = heightMap!;
            Assert.Equal(pristine.GetLength(0), map.Width);
            Assert.Equal(pristine.GetLength(1), map.Height);

            var bridge = game.Scene3D.Bridges[0];
            var layout = bridge.LevelLayout;
            Assert.NotNull(layout);
            var built = layout!;

            var plan = bridge.CorridorPlan;
            Assert.NotNull(plan);
            Assert.Equal(1101.25, plan!.DatumZ, 1e-9);

            var terrain = TerrainTriangles(map);
            var roadwayTris = built.Roadway.Select(ToPlaneTri).ToList();

            var seamMax = 0d;
            foreach (var rowY in new[] { 300d, 310d, 990d, 1000d })
            {
                var rowMax = 0d;
                for (var k = 0; k <= 134; k++)
                {
                    var x = 516d + (k * 0.5d);
                    var deck = DeckZ(built.Roadway, roadwayTris, x, rowY);
                    if (deck is null)
                    {
                        continue;
                    }

                    var ground = TerrainZ(terrain, x, rowY);
                    var gap = Math.Abs(ground - deck.Value);
                    rowMax = Math.Max(rowMax, gap);
                    seamMax = Math.Max(seamMax, gap);
                    Assert.True(gap <= 0.05, $"seam at ({x}, {rowY}) terrain {ground} deck {deck.Value} gap {gap} rowMax {rowMax} seamMax {seamMax}.");
                }
            }

            Assert.InRange(seamMax, 0.03, 0.05);

            var clearanceMin = double.PositiveInfinity;
            for (var y = 320; y <= 980; y++)
            {
                for (var x = 510; x <= 590; x++)
                {
                    var deck = DeckZ(built.Roadway, roadwayTris, x, y);
                    if (deck is null)
                    {
                        continue;
                    }

                    var ground = TerrainZ(terrain, x, y);
                    var clearance = deck.Value - ground;
                    clearanceMin = Math.Min(clearanceMin, clearance);
                    Assert.True(clearance >= 0.98, $"clearance at ({x}, {y}) deck {deck.Value} terrain {ground} clearance {clearance} min {clearanceMin}.");
                }
            }

            Assert.InRange(clearanceMin, 1.00, 1.03);

            for (var y = 300; y <= 1000; y++)
            {
                for (var x = 510; x <= 590; x++)
                {
                    var deck = DeckZ(built.Roadway, roadwayTris, x, y);
                    if (deck is null)
                    {
                        continue;
                    }

                    var ground = TerrainZ(terrain, x, y);
                    Assert.True(ground <= deck.Value + 0.05, $"through deck at ({x}, {y}) terrain {ground} deck {deck.Value}.");
                }
            }

            var joints = new List<double>();
            foreach (var group in built.Roadway.GroupBy(face => (face.PieceName, face.CopyIndex)))
            {
                joints.Add(group.Min(face => (double)face.MinXy.Y));
                joints.Add(group.Max(face => (double)face.MaxXy.Y));
            }

            var holes = 0;
            var doubles = 0;
            for (var xi = 0; xi < 65; xi++)
            {
                var x = 517.5d + xi;
                for (var yi = 0; yi < 694; yi++)
                {
                    var y = 303.5d + yi;
                    var cover = Coverage(roadwayTris, x, y);
                    if (cover < 1)
                    {
                        holes++;
                    }

                    var nearJoint = joints.Any(joint => Math.Abs(y - joint) <= 1.0d);
                    if (!nearJoint && InteriorCount(roadwayTris, x, y) >= 2)
                    {
                        doubles++;
                    }
                }
            }

            Assert.True(holes == 0, $"holes {holes}.");
            Assert.True(doubles == 0, $"double cover {doubles}.");

            for (var i = 0; i < map.Width; i++)
            {
                for (var j = 0; j < map.Height; j++)
                {
                    var position = map.GetPosition(i, j);
                    var inX = position.X >= 510f && position.X <= 590f;
                    var atToe = Math.Abs(position.Y - 0f) <= 1e-6f || Math.Abs(position.Y - 1300f) <= 1e-6f;
                    if (!inX || !atToe)
                    {
                        continue;
                    }

                    var applied = (double)map.GetHeight(i, j);
                    var original = (double)pristine[i, j];
                    var difference = Math.Abs(applied - original);
                    Assert.True(difference <= 1e-6, $"toe continuity at ({position.X}, {position.Y}) pristine {original} applied {applied} diff {difference}.");
                }
            }

            for (var i = 0; i < map.Width; i++)
            {
                for (var j = 0; j < map.Height; j++)
                {
                    var position = map.GetPosition(i, j);
                    var beside = position.X <= 460f || position.X >= 640f;
                    var inY = position.Y >= 0f && position.Y <= 1300f;
                    if (!beside || !inY)
                    {
                        continue;
                    }

                    var applied = (double)map.GetHeight(i, j);
                    var original = (double)pristine[i, j];
                    var difference = Math.Abs(applied - original);
                    Assert.True(difference <= 1e-6, $"untouched at ({position.X}, {position.Y}) pristine {original} applied {applied} diff {difference}.");
                }
            }

            // Per-pair bound derived from the blend, the physical guard that replaces a fixed
            // ceiling: with E the corridor edge target of the row and N_m the pristine height at
            // shoulder column m, the stored shoulder target is floor(E - (m / 5) * D_m) with
            // D_m = E - N_m, so every adjacent pair along the outward sequence has the unquantized
            // step ((m + 1) * D(m + 1) - m * D(m)) / 5 and floor quantization adds at most two
            // elevation steps to a pair. Valid for any native profile.
            Assert.NotNull(plan);

            var elevationStep = BridgeLandingBand.ElevationStep;

            // Measured regression ceiling for this fixture, not an analytic guarantee: the
            // largest measured pair step over all rows and both sides is at most 7.6
            // (measured 7.2656 on 2026-11-26; the formula's fixture ceiling is 7.57).
            var fixtureCeiling = 7.6d;

            foreach (var y in Enumerable.Range(0, 131).Select(k => k * 10d))
            {
                var j = y / 10;

                foreach (var (edgeName, columns, edgeColumn) in new[] { ("west", new[] { 510d, 500d, 490d, 480d, 470d, 460d }, 510), ("east", new[] { 590d, 600d, 610d, 620d, 630d, 640d }, 590) })
                {
                    var edgeIndex = edgeColumn / 10;
                    var edgeTarget = Assert.Single(plan.Targets, t => t.X == edgeIndex && t.Y == j && t.Region != GeneratedBridgeCorridorRegion.Shoulder).Height;
                    var applied = new double[columns.Length];
                    var native = new double[columns.Length];

                    for (var c = 0; c < columns.Length; c++)
                    {
                        var tile = map.GetTilePosition(new Vector2((float)columns[c], (float)y));
                        Assert.NotNull(tile);

                        var key = tile!.Value;
                        applied[c] = map.GetHeight(key.X, key.Y);
                        native[c] = (double)pristine[key.X, key.Y];
                    }

                    var delta = new double[columns.Length];

                    for (var m = 0; m < columns.Length; m++)
                    {
                        delta[m] = edgeTarget - native[m];
                    }

                    for (var m = 0; m + 1 < columns.Length; m++)
                    {
                        var appliedStep = Math.Abs(applied[m + 1] - applied[m]);
                        var unquantizedStep = Math.Abs((((m + 1) * delta[m + 1]) - (m * delta[m])) / 5d);
                        var bound = unquantizedStep + (2d * elevationStep);

                        Assert.True(appliedStep <= bound, $"shoulder pair step at {edgeName} y {y} columns {columns[m]} to {columns[m + 1]} (m {m} to {m + 1}) applied {applied[m]} to {applied[m + 1]} step {appliedStep:F4} bound {bound:F4} unquantized {unquantizedStep:F4}.");
                        Assert.True(appliedStep <= fixtureCeiling, $"shoulder pair step {appliedStep:F4} at {edgeName} y {y} columns {columns[m]} to {columns[m + 1]} exceeds the measured fixture ceiling {fixtureCeiling}.");
                    }
                }
            }

            // Overshoot guard on the applied lattice: every shoulder vertex keeps its applied
            // height between its own native height and its row's edge target, inclusive within
            // one elevation step.
            var shoulderVertexCount = 0;
            var worstExcursion = 0d;

            foreach (var target in plan.Targets)
            {
                if (target.Region != GeneratedBridgeCorridorRegion.Shoulder)
                {
                    continue;
                }

                shoulderVertexCount++;

                var native = (double)pristine[target.X, target.Y];
                var edgeColumn = target.X < 51 ? 51 : 59;
                var edgeTarget = Assert.Single(plan.Targets, t => t.X == edgeColumn && t.Y == target.Y && t.Region != GeneratedBridgeCorridorRegion.Shoulder).Height;
                var applied = (double)map.GetHeight(target.X, target.Y);
                var lower = Math.Min(native, edgeTarget) - elevationStep;
                var upper = Math.Max(native, edgeTarget) + elevationStep;
                var excursion = Math.Max(lower - applied, applied - upper);
                worstExcursion = Math.Max(worstExcursion, excursion);

                Assert.True(
                    applied >= lower && applied <= upper,
                    $"shoulder overshoot at ({target.X},{target.Y}) applied {applied} native {native} edge target {edgeTarget} allowed [{lower}, {upper}].");
            }
        }, withRules: true);
    }

    /// <summary>
    /// Criterion 1 of the deck-end ruling: on the centreline, clearance of at least 0.98 must
    /// first hold continuously within 18 game units inward of each deck model endpoint. The north
    /// figure (about 19.75) is a failure on the current lattice and stays one.
    /// </summary>
    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_CentrelineClearanceWithin18UnitsOfTheDeckEnd()
    {
        WithLoadedGame(game =>
        {
            var map = game.TerrainLogic.HeightMap;
            Assert.NotNull(map);
            var layout = game.Scene3D.Bridges[0].LevelLayout;
            Assert.NotNull(layout);

            var terrain = TerrainTriangles(map);
            var roadway = layout!.Roadway;
            var roadwayTris = roadway.Select(ToPlaneTri).ToList();

            var south = ClearanceCrossing(game, roadway, roadwayTris, terrain, 550d, 300d, 1d);
            var north = ClearanceCrossing(game, roadway, roadwayTris, terrain, 550d, 1000d, -1d);
            Assert.NotNull(south);
            Assert.NotNull(north);

            var summary = $"south distance {south!.Value.DistanceFromEnd:F2} (roadway edge y {south.Value.EdgeY:F2}, crossing y {south.Value.CrossingY:F2}); north distance {north!.Value.DistanceFromEnd:F2} (roadway edge y {north.Value.EdgeY:F2}, crossing y {north.Value.CrossingY:F2}); the gate is 18.";

            Assert.True(south.Value.DistanceFromEnd <= 18d, $"deck-end centreline clearance, south: {summary}");
            Assert.True(north.Value.DistanceFromEnd <= 18d, $"deck-end centreline clearance, north: {summary}");
        }, withRules: true);
    }

    /// <summary>
    /// Criterion 2 of the deck-end ruling: across the full roadway width, clearance of at least
    /// 0.98 must first hold continuously within 18 units inward of each station's actual roadway
    /// edge, comparing only where roadway triangles exist.
    /// </summary>
    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_FullWidthClearanceWithin18UnitsOfEachRoadwayEdge()
    {
        WithLoadedGame(game =>
        {
            var map = game.TerrainLogic.HeightMap;
            Assert.NotNull(map);
            var layout = game.Scene3D.Bridges[0].LevelLayout;
            Assert.NotNull(layout);

            var terrain = TerrainTriangles(map);
            var roadway = layout!.Roadway;
            var roadwayTris = roadway.Select(ToPlaneTri).ToList();

            foreach (var (endY, inward, endName) in new[] { (300d, 1d, "south"), (1000d, -1d, "north") })
            {
                var worstFromEdge = -1d;
                var worstStation = 0d;
                var worstEdgeY = 0d;
                var worstCrossingY = 0d;
                var worstPresentUntil = 0d;

                for (var station = 516.5d; station <= 582.5d; station += 0.5d)
                {
                    var crossing = ClearanceCrossing(game, roadway, roadwayTris, terrain, station, endY, inward);

                    if (crossing is null)
                    {
                        continue;
                    }

                    var fromEdge = Math.Abs(crossing.Value.CrossingY - crossing.Value.EdgeY);

                    if (fromEdge > worstFromEdge)
                    {
                        worstFromEdge = fromEdge;
                        worstStation = station;
                        worstEdgeY = crossing.Value.EdgeY;
                        worstCrossingY = crossing.Value.CrossingY;
                        worstPresentUntil = crossing.Value.RoadwayPresentUntilY;
                    }
                }

                Assert.True(
                    worstFromEdge >= 0d,
                    $"{endName} full-width clearance: no station had a roadway within 60 units inward of the deck model endpoint y {endY}.");

                var worstFromEnd = Math.Abs(worstCrossingY - endY);
                Assert.True(
                    worstFromEdge <= 18d,
                    $"{endName} full-width clearance gate: the worst station is x {worstStation:F1} with {worstFromEdge:F2} game units from its own roadway edge (roadway edge y {worstEdgeY:F2}, crossing y {worstCrossingY:F2}, roadway present until y {worstPresentUntil:F2}); the same crossing is {worstFromEnd:F2} game units from the deck model endpoint y {endY}; the gate is 18.");
            }
        }, withRules: true);
    }

    /// <summary>
    /// The deck model's own tapered end, recorded per the owner's decision: the roadway is absent
    /// between the model endpoint and the edge, so the taper is excluded from the terrain-recovery
    /// criteria and is never a pass of the 18-unit gate. This test records the measured taper.
    /// </summary>
    [GameFact(SageGame.CncGenerals)]
    public void InstalledFixture_DeckEndTaper_Recorded()
    {
        WithLoadedGame(game =>
        {
            var map = game.TerrainLogic.HeightMap;
            Assert.NotNull(map);
            var layout = game.Scene3D.Bridges[0].LevelLayout;
            Assert.NotNull(layout);

            var terrain = TerrainTriangles(map);
            var roadway = layout!.Roadway;
            var roadwayTris = roadway.Select(ToPlaneTri).ToList();

            foreach (var (endY, inward, endName, expectedEdgeY) in new[] { (300d, 1d, "south", 322.60d), (1000d, -1d, "north", 977.40d) })
            {
                var deepest = -1d;
                var deepestStation = 0d;
                var deepestEdgeY = 0d;

                for (var station = 516.5d; station <= 582.5d; station += 0.5d)
                {
                    var edge = RoadwayEdge(roadway, roadwayTris, station, endY, inward);

                    if (edge is null)
                    {
                        continue;
                    }

                    var inwardness = Math.Abs(edge.Value - endY);

                    if (inwardness > deepest)
                    {
                        deepest = inwardness;
                        deepestStation = station;
                        deepestEdgeY = edge.Value;
                    }
                }

                Assert.True(
                    deepest >= 0d,
                    $"{endName} taper recording: no station had a roadway within 60 units inward of the deck model endpoint y {endY}.");

                Assert.InRange(deepest, 22.60d - 0.05d, 22.60d + 0.05d);
                Assert.Equal(582.5d, deepestStation, 5);
                Assert.InRange(deepestEdgeY, expectedEdgeY - 0.05d, expectedEdgeY + 0.05d);
            }
        }, withRules: true);
    }

    /// <summary>
    /// For one station and deck end: the roadway edge y (the first y going inward of the deck
    /// model endpoint where the roadway is defined) and the first y going inward where clearance
    /// is at least 0.98 continuously, compared only where roadway triangles exist: the scan
    /// follows the roadway up to the deck midpoint (y 650, steps of 0.1) and stops where the
    /// roadway ends; null when no roadway exists within 60 units inward. Also returns the y up to
    /// which the roadway was present.
    /// </summary>
    private static (double EdgeY, double CrossingY, double DistanceFromEnd, double RoadwayPresentUntilY)? ClearanceCrossing(Game game, IReadOnlyList<BridgeSurface.BridgeRawFace> roadway, List<PlaneTri> roadwayTris, List<PlaneTri> terrain, double stationX, double endY, double inward)
    {
        var edgeY = RoadwayEdge(roadway, roadwayTris, stationX, endY, inward);

        if (edgeY is null)
        {
            return null;
        }

        var lastFail = -1d;
        var presentUntil = edgeY.Value;

        for (var offset = 0d; ; offset += 0.1d)
        {
            var y = edgeY.Value + (inward * offset);

            if (inward > 0d ? y > 650d : y < 650d)
            {
                break;
            }

            var deck = DeckZ(roadway, roadwayTris, stationX, y);

            // The roadway has ended at this station: stop comparing there.
            if (deck is null)
            {
                break;
            }

            presentUntil = y;

            if (deck.Value - TerrainZ(terrain, stationX, y) < 0.98d)
            {
                lastFail = offset;
            }
        }

        var crossingOffset = lastFail < 0d ? 0d : lastFail + 0.1d;
        var crossingY = edgeY.Value + (inward * crossingOffset);

        return (edgeY.Value, crossingY, Math.Abs(crossingY - endY), presentUntil);
    }

    /// <summary>The roadway end edge at one station: the first y going inward where the roadway is defined; null when none within 60 units.</summary>
    private static double? RoadwayEdge(IReadOnlyList<BridgeSurface.BridgeRawFace> roadway, List<PlaneTri> roadwayTris, double stationX, double endY, double inward)
    {
        var edgeY = endY - (inward * 10d);
        var guard = 0;

        while (DeckZ(roadway, roadwayTris, stationX, edgeY) is null)
        {
            edgeY += inward * 0.1d;

            if (++guard > 600)
            {
                return null;
            }
        }

        return edgeY;
    }

    private static float[,] SnapshotPristine()
    {
        float[,]? snapshot = null;
        WithLoadedGame(game =>
        {
            var heightMap = game.TerrainLogic.HeightMap;
            Assert.NotNull(heightMap);
            var map = heightMap!;
            var taken = new float[map.Width, map.Height];
            for (var i = 0; i < map.Width; i++)
            {
                for (var j = 0; j < map.Height; j++)
                {
                    taken[i, j] = map.GetHeight(i, j);
                }
            }

            snapshot = taken;
        }, withRules: false);
        Assert.NotNull(snapshot);
        return snapshot!;
    }

    private static PlaneTri ToPlaneTri(BridgeSurface.BridgeRawFace face)
    {
        return new PlaneTri(face.A.X, face.A.Y, face.A.Z, face.B.X, face.B.Y, face.B.Z, face.C.X, face.C.Y, face.C.Z);
    }

    private static List<PlaneTri> TerrainTriangles(HeightMap heightMap)
    {
        var result = new List<PlaneTri>();
        for (var j = 0; j < heightMap.Height - 1; j++)
        {
            for (var i = 0; i < heightMap.Width - 1; i++)
            {
                var p00 = heightMap.GetPosition(i, j);
                var p10 = heightMap.GetPosition(i + 1, j);
                var p01 = heightMap.GetPosition(i, j + 1);
                var p11 = heightMap.GetPosition(i + 1, j + 1);
                var minX = Math.Min(Math.Min(p00.X, p10.X), Math.Min(p01.X, p11.X));
                var maxX = Math.Max(Math.Max(p00.X, p10.X), Math.Max(p01.X, p11.X));
                var minY = Math.Min(Math.Min(p00.Y, p10.Y), Math.Min(p01.Y, p11.Y));
                var maxY = Math.Max(Math.Max(p00.Y, p10.Y), Math.Max(p01.Y, p11.Y));
                if (maxX < 470d || minX > 630d || maxY < -10d || minY > 1310d)
                {
                    continue;
                }

                result.Add(new PlaneTri(p00.X, p00.Y, p00.Z, p10.X, p10.Y, p10.Z, p01.X, p01.Y, p01.Z));
                result.Add(new PlaneTri(p01.X, p01.Y, p01.Z, p10.X, p10.Y, p10.Z, p11.X, p11.Y, p11.Z));
            }
        }

        return result;
    }

    private static double TerrainZ(IReadOnlyList<PlaneTri> terrain, double x, double y)
    {
        foreach (var tri in terrain)
        {
            if (x < tri.MinX || x > tri.MaxX || y < tri.MinY || y > tri.MaxY)
            {
                continue;
            }

            if (tri.Covers(x, y, 1e-9d))
            {
                return tri.ZAt(x, y);
            }
        }

        Assert.Fail($"no terrain triangle contains ({x}, {y}).");
        throw new InvalidOperationException("unreachable");
    }

    private static double? DeckZ(IReadOnlyList<BridgeSurface.BridgeRawFace> roadway, IReadOnlyList<PlaneTri> tris, double x, double y)
    {
        double? best = null;
        for (var index = 0; index < roadway.Count; index++)
        {
            var tri = tris[index];
            if (x < tri.MinX || x > tri.MaxX || y < tri.MinY || y > tri.MaxY)
            {
                continue;
            }

            if (!tri.Covers(x, y, 1e-3d))
            {
                continue;
            }

            var z = (double)roadway[index].PlaneZ((float)x, (float)y);
            if (double.IsNaN(z))
            {
                continue;
            }

            best = best is null ? z : Math.Min(best.Value, z);
        }

        return best;
    }

    private static int Coverage(IReadOnlyList<PlaneTri> tris, double x, double y)
    {
        var count = 0;
        foreach (var tri in tris)
        {
            if (x < tri.MinX || x > tri.MaxX || y < tri.MinY || y > tri.MaxY)
            {
                continue;
            }

            if (tri.Covers(x, y, 1e-3d))
            {
                count++;
            }
        }

        return count;
    }

    private static int InteriorCount(IReadOnlyList<PlaneTri> tris, double x, double y)
    {
        var count = 0;
        foreach (var tri in tris)
        {
            if (x < tri.MinX || x > tri.MaxX || y < tri.MinY || y > tri.MaxY)
            {
                continue;
            }

            if (!tri.IsValid)
            {
                continue;
            }

            tri.Weights(x, y, out var w0, out var w1, out var w2);
            if (w0 > 1e-6d && w1 > 1e-6d && w2 > 1e-6d)
            {
                count++;
            }
        }

        return count;
    }

    private sealed class PlaneTri
    {
        public readonly double[] X;

        public readonly double[] Y;

        public readonly double[] Z;

        public readonly double MinX;

        public readonly double MaxX;

        public readonly double MinY;

        public readonly double MaxY;

        private readonly double _denominator;

        public PlaneTri(double x0, double y0, double z0, double x1, double y1, double z1, double x2, double y2, double z2)
        {
            var area2 = ((x1 - x0) * (y2 - y0)) - ((x2 - x0) * (y1 - y0));
            if (area2 >= 0d)
            {
                X = [x0, x1, x2];
                Y = [y0, y1, y2];
                Z = [z0, z1, z2];
            }
            else
            {
                X = [x0, x2, x1];
                Y = [y0, y2, y1];
                Z = [z0, z2, z1];
            }

            MinX = Math.Min(X[0], Math.Min(X[1], X[2]));
            MaxX = Math.Max(X[0], Math.Max(X[1], X[2]));
            MinY = Math.Min(Y[0], Math.Min(Y[1], Y[2]));
            MaxY = Math.Max(Y[0], Math.Max(Y[1], Y[2]));
            _denominator = ((Y[1] - Y[2]) * (X[0] - X[2])) + ((X[2] - X[1]) * (Y[0] - Y[2]));
        }

        public bool IsValid => Math.Abs(_denominator) > 1e-18d;

        public void Weights(double x, double y, out double w0, out double w1, out double w2)
        {
            w0 = (((Y[1] - Y[2]) * (x - X[2])) + ((X[2] - X[1]) * (y - Y[2]))) / _denominator;
            w1 = (((Y[2] - Y[0]) * (x - X[2])) + ((X[0] - X[2]) * (y - Y[2]))) / _denominator;
            w2 = 1d - w0 - w1;
        }

        public double ZAt(double x, double y)
        {
            Weights(x, y, out var w0, out var w1, out var w2);
            return (w0 * Z[0]) + (w1 * Z[1]) + (w2 * Z[2]);
        }

        public bool Covers(double x, double y, double tolerance)
        {
            if (!IsValid)
            {
                return false;
            }

            Weights(x, y, out var w0, out var w1, out var w2);
            return w0 >= -tolerance && w1 >= -tolerance && w2 >= -tolerance;
        }
    }

    private static void WithLoadedGame(Action<Game> body, bool withRules)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-corridor-gates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            Convert(payloadDir, package, withRules);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(package, [], GameType.SinglePlayer);
            if (withRules)
            {
                Assert.NotEmpty(game.Scene3D.Bridges);
            }

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
            "r3-g8-corridor-gates",
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
