#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Content.Importing.W3d;
using Panopticon.Content.Loaders;
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
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Installed handoff proof: the dozer drives the loader-placed level bridge in both directions
/// while every tyre's contact band is measured frame by frame against the exact support (terrain
/// and deck triangles through the production corridor pose), and a pose-off rerun proves the
/// simulation itself is unchanged.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeHandoffTests
{
    private const string DozerDefinitionName = "AmericaVehicleDozer";

    private const double GateAbsCentroid = 0.50d;

    private const double GateFootprintMin = -0.50d;

    private const double GateTiltChangeDegrees = 1.0d;

    // A fixture-specific regression guard (measured 29.0 on the feathered lattice against 55.0
    // unfeathered), not a general safe-tilt claim; the tyre contact gates (0.50) are separate
    // assertions.
    private const double GateLateralTiltDegrees = 30.0d;

    [GameFact(SageGame.CncGenerals)]
    public void DozerHandoff_Northbound_AllFourTyresWithinGates()
    {
        WithLoadedGame((game, _) =>
        {
            var run = Record(game, new Vector3(550f, 150f, 0f), new Vector3(550f, 1380f, 0f), position => position.Y >= 1376f, 1500);

            Assert.True(run.ReachedGoal, $"the run stopped early at y {run.Frames[^1].Origin.Y}.");
            Assert.True(run.Frames[^1].Origin.Y >= 1376f, $"the dozer reached only y {run.Frames[^1].Origin.Y}.");

            // The ALL window: from the first frame through the last frame at which any band
            // vertex is still at or before the north toe (y 1300).
            var lastToe = 0;

            for (var index = 0; index < run.Frames.Count; index++)
            {
                if (run.Frames[index].Tyres.Any(tyre => tyre.Band.Any(vertex => vertex.Y <= 1300d)))
                {
                    lastToe = index;
                }
            }

            Assert.True(lastToe + 1 >= 1100, $"the ALL window covers only {lastToe + 1} frames.");
            CheckAllTyres(run, 0, lastToe);
            CheckNormalContinuity(run, 0, lastToe);

            var bridge = game.Scene3D.Bridges[0];
            Assert.NotNull(bridge.DeckModel);

            var layout = bridge.LevelLayout;
            Assert.NotNull(layout);

            var meshTriangles = new HashSet<string>(StringComparer.Ordinal);

            foreach (var subObject in bridge.DeckModel!.Imported.SubObjects)
            {
                if (subObject.RenderObject is not ImportedMesh mesh)
                {
                    continue;
                }

                for (var triangle = 0; triangle < mesh.Indices.Count / 3; triangle++)
                {
                    meshTriangles.Add(TriangleKey(
                        mesh.Vertices[mesh.Indices[3 * triangle]].Position,
                        mesh.Vertices[mesh.Indices[(3 * triangle) + 1]].Position,
                        mesh.Vertices[mesh.Indices[(3 * triangle) + 2]].Position));
                }
            }

            foreach (var face in layout!.Roadway)
            {
                Assert.Contains(TriangleKey(face.A, face.B, face.C), meshTriangles);
            }

            Assert.Equal(Matrix4x4.Identity, bridge.DeckModel.Instance.GetWorldMatrix());
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerHandoff_Southbound_AllFourTyresWithinGates()
    {
        WithLoadedGame((game, _) =>
        {
            var run = Record(game, new Vector3(550f, 1380f, 0f), new Vector3(550f, 120f, 0f), null, 1500);

            // The ALL window: from the first frame at which any band vertex has entered the
            // corridor (y at most 1300) through the last frame.
            var firstToe = -1;

            for (var index = 0; index < run.Frames.Count && firstToe < 0; index++)
            {
                if (run.Frames[index].Tyres.Any(tyre => tyre.Band.Any(vertex => vertex.Y <= 1300d)))
                {
                    firstToe = index;
                }
            }

            Assert.True(firstToe >= 0, "no frame has a band vertex at or south of the north toe.");
            CheckAllTyres(run, firstToe, run.Frames.Count - 1);
            CheckNormalContinuity(run, firstToe, run.Frames.Count - 1);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerLateralCrossing_AllFourTyresWithinGatesOnBothEdges()
    {
        WithLoadedGame((game, _) =>
        {
            var run = Record(game, new Vector3(430f, 150f, 0f), new Vector3(670f, 150f, 0f), position => position.X >= 660f, 600);

            Assert.True(run.ReachedGoal, $"the run stopped early at x {run.Frames[^1].Origin.X}.");
            Assert.True(run.Frames[^1].Origin.X >= 660f, $"the dozer reached only x {run.Frames[^1].Origin.X}.");

            var westFrames = new List<int>();
            var eastFrames = new List<int>();

            for (var index = 0; index < run.Frames.Count; index++)
            {
                if (run.Frames[index].Tyres.Any(tyre => tyre.Band.Any(vertex => vertex.X >= 465d && vertex.X <= 515d)))
                {
                    westFrames.Add(index);
                }

                if (run.Frames[index].Tyres.Any(tyre => tyre.Band.Any(vertex => vertex.X >= 585d && vertex.X <= 635d)))
                {
                    eastFrames.Add(index);
                }
            }

            Assert.True(westFrames.Count > 0, "the west edge window is empty.");
            Assert.True(eastFrames.Count > 0, "the east edge window is empty.");

            CheckWindowTyres(run, westFrames, "westEdge");
            CheckWindowTyres(run, eastFrames, "eastEdge");

            var worstTilt = 0d;
            var worstAt = -1;

            foreach (var index in westFrames.Concat(eastFrames).Distinct())
            {
                var tilt = TiltDegrees((0d, 0d, 1d), run.Frames[index].Normal);

                if (tilt > worstTilt)
                {
                    worstTilt = tilt;
                    worstAt = index;
                }
            }

            Assert.True(worstTilt <= GateLateralTiltDegrees, $"plane-normal tilt {worstTilt:F4} degrees at frame {worstAt} exceeds {GateLateralTiltDegrees}.");
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void PoseChangesNothingInTheSimulation()
    {
        WithLoadedGame((game, package) =>
        {
            // Both runs start from the same state: a fresh reload before each, so the only
            // difference is the pose toggle.
            RunResult RunNorth(bool poseEnabled)
            {
                game.LoadMap(package, [], GameType.SinglePlayer);
                Drawable.BridgeWheelPoseEnabled = poseEnabled;

                try
                {
                    // Both runs go to the frame limit so the traces have the same length; the
                    // dozer parks at its goal well before the limit.
                    return Record(game, new Vector3(550f, 150f, 0f), new Vector3(550f, 1380f, 0f), null, 1500);
                }
                finally
                {
                    Drawable.BridgeWheelPoseEnabled = true;
                }
            }

            var poseOff = RunNorth(false);
            var poseOn = RunNorth(true);

            Assert.Equal(poseOff.Frames.Count, poseOn.Frames.Count);
            var common = Math.Min(poseOff.Frames.Count, poseOn.Frames.Count);

            for (var index = 0; index < common; index++)
            {
                var off = poseOff.Frames[index].Origin;
                var on = poseOn.Frames[index].Origin;
                Assert.True(
                    MathF.Abs(off.X - on.X) <= 1e-5f && MathF.Abs(off.Y - on.Y) <= 1e-5f && MathF.Abs(off.Z - on.Z) <= 1e-5f,
                    $"logic trace diverges at frame {index}: pose-off ({off.X}, {off.Y}, {off.Z}) pose-on ({on.X}, {on.Y}, {on.Z}).");
            }

            // Without the pose the body rides the raw terrain pitch and roll, so the worst
            // tyre-to-support fit over the ALL window must be strictly worse than with the pose.
            var poseOffWorst = LargestAbsGap(poseOff, 0, LastToeFrame(poseOff));
            var poseOnWorst = LargestAbsGap(poseOn, 0, LastToeFrame(poseOn));

            Assert.True(poseOffWorst > poseOnWorst, $"pose-off worst |gap| {poseOffWorst:F4} is not worse than pose-on {poseOnWorst:F4}.");
        });
    }

    private static int LastToeFrame(RunResult run)
    {
        var lastToe = 0;

        for (var index = 0; index < run.Frames.Count; index++)
        {
            if (run.Frames[index].Tyres.Any(tyre => tyre.Band.Any(vertex => vertex.Y <= 1300d)))
            {
                lastToe = index;
            }
        }

        return lastToe;
    }

    private static void CheckAllTyres(RunResult run, int firstFrame, int lastFrame)
    {
        var worstAbsGap = 0d;
        var worstAbsGapTyre = string.Empty;
        var worstAbsGapFrame = 0;
        var worstFootprint = double.MaxValue;
        var worstFootprintTyre = string.Empty;
        var worstFootprintFrame = 0;

        for (var index = firstFrame; index <= lastFrame; index++)
        {
            var frame = run.Frames[index];
            Assert.Equal(4, frame.Tyres.Length);

            foreach (var tyre in frame.Tyres)
            {
                var absGap = Math.Abs(tyre.Gap);

                if (absGap > worstAbsGap)
                {
                    worstAbsGap = absGap;
                    worstAbsGapTyre = tyre.Name;
                    worstAbsGapFrame = index;
                }

                if (tyre.FootprintMin < worstFootprint)
                {
                    worstFootprint = tyre.FootprintMin;
                    worstFootprintTyre = tyre.Name;
                    worstFootprintFrame = index;
                }
            }
        }

        Assert.True(
            worstAbsGap <= GateAbsCentroid,
            $"centroid gate: largest |gap| {worstAbsGap:F4} on tyre '{worstAbsGapTyre}' at frame {worstAbsGapFrame} exceeds {GateAbsCentroid}.");
        Assert.True(
            worstFootprint >= GateFootprintMin,
            $"footprint gate: smallest footprintMin {worstFootprint:F4} on tyre '{worstFootprintTyre}' at frame {worstFootprintFrame} is below {GateFootprintMin}.");
    }

    private static void CheckWindowTyres(RunResult run, IReadOnlyList<int> frames, string windowName)
    {
        var worstAbsGap = 0d;
        var worstAbsGapTyre = string.Empty;
        var worstAbsGapFrame = 0;
        var worstFootprint = double.MaxValue;
        var worstFootprintTyre = string.Empty;
        var worstFootprintFrame = 0;

        foreach (var index in frames)
        {
            var frame = run.Frames[index];
            Assert.Equal(4, frame.Tyres.Length);

            foreach (var tyre in frame.Tyres)
            {
                var absGap = Math.Abs(tyre.Gap);

                if (absGap > worstAbsGap)
                {
                    worstAbsGap = absGap;
                    worstAbsGapTyre = tyre.Name;
                    worstAbsGapFrame = index;
                }

                if (tyre.FootprintMin < worstFootprint)
                {
                    worstFootprint = tyre.FootprintMin;
                    worstFootprintTyre = tyre.Name;
                    worstFootprintFrame = index;
                }
            }
        }

        Assert.True(
            worstAbsGap <= GateAbsCentroid,
            $"{windowName} centroid gate: largest |gap| {worstAbsGap:F4} on tyre '{worstAbsGapTyre}' at frame {worstAbsGapFrame} exceeds {GateAbsCentroid}.");
        Assert.True(
            worstFootprint >= GateFootprintMin,
            $"{windowName} footprint gate: smallest footprintMin {worstFootprint:F4} on tyre '{worstFootprintTyre}' at frame {worstFootprintFrame} is below {GateFootprintMin}.");
    }

    private static void CheckNormalContinuity(RunResult run, int firstFrame, int lastFrame)
    {
        var worst = 0d;
        var worstAt = firstFrame;

        for (var index = firstFrame + 1; index <= lastFrame; index++)
        {
            var tilt = TiltDegrees(run.Frames[index - 1].Normal, run.Frames[index].Normal);

            if (tilt > worst)
            {
                worst = tilt;
                worstAt = index;
            }
        }

        Assert.True(worst <= GateTiltChangeDegrees, $"plane normal tilt change {worst:F4} degrees at frame {worstAt} exceeds {GateTiltChangeDegrees}.");
    }

    private static double LargestAbsGap(RunResult run, int firstFrame, int lastFrame)
    {
        var worst = 0d;

        for (var index = firstFrame; index <= lastFrame; index++)
        {
            foreach (var tyre in run.Frames[index].Tyres)
            {
                worst = Math.Max(worst, Math.Abs(tyre.Gap));
            }
        }

        return worst;
    }

    private RunResult Record(Game game, Vector3 start, Vector3 goal, Func<Vector3, bool>? stopPredicate, int maxFrames)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var definition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
        Assert.NotNull(definition);

        var dozer = game.Scene3D.GameObjects.CreateObject(definition!, player);
        var startZ = game.TerrainLogic.GetGroundHeight(start.X, start.Y);
        dozer.SetTranslation(new Vector3(start.X, start.Y, startZ));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        // One throwaway frame flushes the fixed-timestep accumulator left over from map load,
        // before the move order exists so the dozer is idle and both recorded runs start on
        // the same logic clock.
        game.Update(Array.Empty<InputMessage>());

        player.SelectUnits([dozer]);

        var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), goal);
        new OrderProcessor(game).Process([order]);

        var terrain = BucketTerrain(TerrainTriangles(game.TerrainLogic.HeightMap));
        var importCache = new Dictionary<string, ImportedModel?>(StringComparer.Ordinal);
        var frames = new List<FrameSample>(maxFrames);
        var reached = stopPredicate is null;
        var tyreCount = 0;

        for (var frame = 0; frame < maxFrames; frame++)
        {
            game.Update(Array.Empty<InputMessage>());

            var position = dozer.Translation;
            var eye = new Vector3(position.X - 40f, position.Y - 50f, position.Z + 45f);
            var look = new Vector3(position.X, position.Y - 6f, position.Z - 3f);
            game.Scene3D.Camera.SetLookAt(eye, look, Vector3.UnitZ);
            game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
            game.Render();

            var tyres = new List<TyreSample>();
            var normal = (0d, 0d, 1d);
            var modelInstance = FirstW3dModelInstance(dozer);

            if (modelInstance != null)
            {
                tyres = DiscoverTyres(game, modelInstance, importCache, terrain);
                tyreCount = tyres.Count;

                if (tyres.Count == 4)
                {
                    normal = PlaneNormalFromPoints([.. tyres.Select(tyre => (tyre.CentroidX, tyre.CentroidY, tyre.CentroidZ))]);
                }
            }

            frames.Add(new FrameSample(frame, position, normal, [.. tyres]));

            if (stopPredicate is { } predicate && predicate(position))
            {
                reached = true;
                break;
            }
        }

        return new RunResult(frames, tyreCount, reached);
    }

    /// <summary>
    /// The dozer's tyre contact bands in world space: CPU import cached per model, non-skinned
    /// non-hidden sub-objects whose name contains TIRE, every vertex within 0.5 of the tyre's
    /// lowest posed vertex, support and gaps against the exact deck-and-terrain support.
    /// </summary>
    private List<TyreSample> DiscoverTyres(Game game, ModelInstance modelInstance, Dictionary<string, ImportedModel?> importCache, Dictionary<int, List<PlaneTri>> terrain)
    {
        var model = modelInstance.Model;

        if (!importCache.TryGetValue(model.Name, out var imported))
        {
            var importer = new W3dModelImporter(PathResolvers.W3d);
            imported = importer.ImportModel(model.Name, new AssetImportContext(game.AssetStore.LoadContext.FileSystem, game.AssetStore.LoadContext.Language));
            importCache.Add(model.Name, imported);
        }

        var tyres = new List<TyreSample>();

        for (var index = 0; index < model.SubObjects.Length; index++)
        {
            var subObject = model.SubObjects[index];

            if (subObject.RenderObject is not ModelMesh mesh || mesh.Skinned || mesh.Hidden)
            {
                continue;
            }

            if (!subObject.FullName.Contains("TIRE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (imported?.SubObjects[index].RenderObject is not ImportedMesh importedMesh)
            {
                continue;
            }

            var boneIndex = subObject.Bone?.Index ?? 0;
            var transform = modelInstance.AbsoluteBoneTransforms[boneIndex];
            var world = new Vector3[importedMesh.Vertices.Count];

            for (var vertexIndex = 0; vertexIndex < importedMesh.Vertices.Count; vertexIndex++)
            {
                world[vertexIndex] = Vector3.Transform(importedMesh.Vertices[vertexIndex].Position, transform);
            }

            var minZ = float.MaxValue;

            foreach (var vertex in world)
            {
                minZ = MathF.Min(minZ, vertex.Z);
            }

            var band = world.Where(vertex => vertex.Z <= minZ + 0.5f).ToArray();
            double centroidX = 0d;
            double centroidY = 0d;
            double centroidZ = 0d;
            double footprintMin = double.MaxValue;

            foreach (var vertex in band)
            {
                centroidX += vertex.X;
                centroidY += vertex.Y;
                centroidZ += vertex.Z;
                footprintMin = Math.Min(footprintMin, vertex.Z - SupportAt(game, terrain, vertex.X, vertex.Y));
            }

            centroidX /= band.Length;
            centroidY /= band.Length;
            centroidZ /= band.Length;

            var gap = centroidZ - SupportAt(game, terrain, centroidX, centroidY);
            tyres.Add(new TyreSample(subObject.FullName, band, centroidX, centroidY, centroidZ, gap, footprintMin));
        }

        return tyres;
    }

    /// <summary>Exact support: the larger of the rendered terrain height and the registered deck surface height.</summary>
    private static double SupportAt(Game game, Dictionary<int, List<PlaneTri>> terrain, double x, double y)
    {
        var support = TerrainZ(terrain, x, y);
        var deck = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight((float)x, (float)y);

        return deck is { } deckZ ? Math.Max(support, deckZ) : support;
    }

    private static ModelInstance? FirstW3dModelInstance(GameObject gameObject)
    {
        if (gameObject.Client is not Drawable drawable)
        {
            return null;
        }

        foreach (var module in drawable.DrawModules)
        {
            if (module is W3dModelDraw w3dModule)
            {
                return w3dModule.ActiveModelInstanceOrNull;
            }
        }

        return null;
    }

    private static (double X, double Y, double Z) PlaneNormalFromPoints((double X, double Y, double Z)[] points)
    {
        double mx = 0d;
        double my = 0d;
        double mz = 0d;

        foreach (var point in points)
        {
            mx += point.X;
            my += point.Y;
            mz += point.Z;
        }

        mx /= points.Length;
        my /= points.Length;
        mz /= points.Length;

        var scatter = new double[3, 3];

        foreach (var point in points)
        {
            var dx = point.X - mx;
            var dy = point.Y - my;
            var dz = point.Z - mz;
            scatter[0, 0] += dx * dx;
            scatter[0, 1] += dx * dy;
            scatter[0, 2] += dx * dz;
            scatter[1, 1] += dy * dy;
            scatter[1, 2] += dy * dz;
            scatter[2, 2] += dz * dz;
        }

        scatter[1, 0] = scatter[0, 1];
        scatter[2, 0] = scatter[0, 2];
        scatter[2, 1] = scatter[1, 2];

        var (eigenValues, eigenVectors) = JacobiEigen(scatter);
        var smallest = 0;

        for (var i = 1; i < 3; i++)
        {
            if (eigenValues[i] < eigenValues[smallest])
            {
                smallest = i;
            }
        }

        var nx = eigenVectors[smallest][0];
        var ny = eigenVectors[smallest][1];
        var nz = eigenVectors[smallest][2];

        if (nz < 0d)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }

        if (Math.Abs(nz) < 1e-9d)
        {
            return (0d, 0d, 1d);
        }

        var len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        return (nx / len, ny / len, nz / len);
    }

    /// <summary>Cyclic Jacobi eigen-decomposition of a symmetric 3x3 matrix (deterministic).</summary>
    private static (double[] Values, double[][] Vectors) JacobiEigen(double[,] matrix)
    {
        var a = new double[3, 3];
        var v = new double[3, 3];

        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                a[i, j] = matrix[i, j];
            }

            v[i, i] = 1d;
        }

        for (var sweep = 0; sweep < 64; sweep++)
        {
            var off = Math.Max(Math.Abs(a[0, 1]), Math.Max(Math.Abs(a[0, 2]), Math.Abs(a[1, 2])));

            if (off < 1e-14d)
            {
                break;
            }

            for (var p = 0; p < 2; p++)
            {
                for (var q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-18d)
                    {
                        continue;
                    }

                    var theta = (a[q, q] - a[p, p]) / (2d * a[p, q]);
                    var sign = theta >= 0d ? 1d : -1d;
                    var t = sign / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1d));
                    var c = 1d / Math.Sqrt((t * t) + 1d);
                    var s = t * c;

                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < 3; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        var values = new double[3];
        var vectors = new double[3][];

        for (var i = 0; i < 3; i++)
        {
            values[i] = a[i, i];
            var norm = Math.Sqrt((v[0, i] * v[0, i]) + (v[1, i] * v[1, i]) + (v[2, i] * v[2, i]));
            vectors[i] = norm > 1e-30d
                ? [v[0, i] / norm, v[1, i] / norm, v[2, i] / norm]
                : [1d, 0d, 0d];
        }

        return (values, vectors);
    }

    private static double TiltDegrees((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var dot = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        return Math.Acos(Math.Clamp(dot, -1d, 1d)) * (180d / Math.PI);
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

                if (maxX < 400d || minX > 700d || maxY < -10d || minY > 1450d)
                {
                    continue;
                }

                result.Add(new PlaneTri(p00.X, p00.Y, p00.Z, p10.X, p10.Y, p10.Z, p01.X, p01.Y, p01.Z));
                result.Add(new PlaneTri(p01.X, p01.Y, p01.Z, p10.X, p10.Y, p10.Z, p11.X, p11.Y, p11.Z));
            }
        }

        return result;
    }

    /// <summary>Buckets the terrain triangles by their y cell so per-vertex support queries stay cheap over long runs.</summary>
    private static Dictionary<int, List<PlaneTri>> BucketTerrain(IReadOnlyList<PlaneTri> triangles)
    {
        var buckets = new Dictionary<int, List<PlaneTri>>();

        foreach (var triangle in triangles)
        {
            var first = (int)Math.Floor(triangle.MinY / 10d);
            var last = (int)Math.Floor(triangle.MaxY / 10d);

            for (var cell = first; cell <= last; cell++)
            {
                if (!buckets.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    buckets.Add(cell, bucket);
                }

                bucket.Add(triangle);
            }
        }

        return buckets;
    }

    private static double TerrainZ(Dictionary<int, List<PlaneTri>> buckets, double x, double y)
    {
        var cell = (int)Math.Floor(y / 10d);

        for (var neighbour = cell - 1; neighbour <= cell + 1; neighbour++)
        {
            if (!buckets.TryGetValue(neighbour, out var bucket))
            {
                continue;
            }

            foreach (var tri in bucket)
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
        }

        Assert.Fail($"no terrain triangle contains ({x}, {y}).");
        throw new InvalidOperationException("unreachable");
    }

    private static string TriangleKey(Vector3 a, Vector3 b, Vector3 c)
    {
        return string.Join("|", new[] { a, b, c }.Select(vertex => string.Join(",", new[] { vertex.X, vertex.Y, vertex.Z }
            .Select(component => component.ToString("R", CultureInfo.InvariantCulture)))));
    }

    private static void WithLoadedGame(Action<Game, string> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-handoff-proof", Guid.NewGuid().ToString("N"));
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
            body(game, package);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private sealed record TyreSample(string Name, Vector3[] Band, double CentroidX, double CentroidY, double CentroidZ, double Gap, double FootprintMin);

    private sealed record FrameSample(int Frame, Vector3 Origin, (double X, double Y, double Z) Normal, TyreSample[] Tyres);

    private sealed record RunResult(List<FrameSample> Frames, int TyreCount, bool ReachedGoal);

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

    // ------------------------------------------------------------------
    // Fixture helpers (copied from GeneratedPackageBridgePathTests).
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
            "r3-g8-handoff-proof",
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
