using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// B4a consumer measurements for the WorldBuilder editing pipeline. Each repeat runs five stages on
/// a fresh map: author (bulk placement and one height-brush stroke on a map built from heights),
/// region edit (select, move, delete, undo), save (directory package), load (package reopen), and
/// play (<see cref="IB4aPlayRuntime"/>). Each stage invocation is one B1 tick bracket and one named
/// system span. No install is required when the play runtime is install-free.
/// </summary>
public static class B4aConsumerBenchmark
{
    private const string ScenarioId = "b4a.consumer";
    private const uint Seed = 1;

    private const string AuthorStage = "b4a.author";
    private const string RegionEditStage = "b4a.regionEdit";
    private const string SaveStage = "b4a.save";
    private const string LoadStage = "b4a.load";
    private const string PlayStage = "b4a.play";

    private const int PlayableSize = 500;
    private const int BorderWidth = 6;
    private const int LatticeSize = PlayableSize + (2 * BorderWidth) + 1;
    private const float WorldWidth = PlayableSize * HeightMap.HorizontalScale;
    private const float Spacing = 20f;
    private const string ObjectTypeName = "AmericaTankCrusader";

    public static B4aConsumerRun Run(B4aConsumerPlan plan, IB4aPlayRuntime play)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(play);
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.Repeats, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.ObjectCount, 16);

        var root = Path.Combine(Path.GetTempPath(), $"b4a-consumer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var meter = new B4aStageMeter();
            var observations = new List<B4aRepeatObservation>(plan.Repeats);
            var totalStopwatch = Stopwatch.StartNew();

            for (var repeat = 0; repeat < plan.Repeats; repeat++)
            {
                var packageDirectory = Path.Combine(root, $"repeat-{repeat}", "package");
                observations.Add(RunRepeat(meter, plan, play, packageDirectory));
            }

            totalStopwatch.Stop();

            var record = new BenchmarkRunRecord(
                ScenarioId,
                Seed,
                warmupFrames: 0,
                measuredFrames: plan.Repeats * plan.PlayFrames,
                framesCompleted: observations.Sum(o => o.PlayFramesCompleted),
                stopPredicateTriggered: false,
                measuredWallMs: totalStopwatch.Elapsed.TotalMilliseconds)
            {
                TickSamples = meter.TickSamples.ToList(),
                SystemSamples = meter.SystemSamples.ToList(),
                Environment = EnvironmentRecord.Capture(),
            };
            record.Statistics.AddRange(SimulationBenchmarkRunner.BuildStatistics(record, Seed, baseline: null));

            using var process = Process.GetCurrentProcess();
            return new B4aConsumerRun(record, meter.Summaries(), observations, process.PeakWorkingSet64);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static B4aRepeatObservation RunRepeat(
        B4aStageMeter meter,
        B4aConsumerPlan plan,
        IB4aPlayRuntime play,
        string packageDirectory)
    {
        var (map, brushCenterDelta) = meter.Measure(AuthorStage, () => Author(plan));
        var authoredCount = map.MapFile.ObjectsList.Objects.Length;

        var region = meter.Measure(RegionEditStage, () => RegionEdit(map, plan));

        var savedObjects = map.MapFile.ObjectsList.Objects.Select(o => (o.TypeName, o.Position)).ToArray();
        meter.Measure(SaveStage, () => map.SaveAsPackage(packageDirectory));
        var loaded = meter.Measure(LoadStage, () => EditorMap.OpenPackage(packageDirectory));
        var played = meter.Measure(PlayStage, () => play.RunFrames(packageDirectory, plan.PlayFrames, Seed));

        var loadedObjects = loaded.MapFile.ObjectsList.Objects;
        return new B4aRepeatObservation(
            authoredCount,
            brushCenterDelta,
            region.Moved,
            region.Deleted,
            region.CountAfterDelete,
            region.CountAfterUndo,
            region.Restored,
            loadedObjects.Length,
            LoadedMatchesSaved(savedObjects, loadedObjects),
            played);
    }

    private static (EditorMap Map, int BrushCenterDelta) Author(B4aConsumerPlan plan)
    {
        var mapFile = MapFile.CreateFromHeights(BuildHeights(), BorderWidth);
        var map = EditorMap.CreateNew(mapFile);

        var placer = new PlaceObjectTool { SelectedObjectType = ObjectTypeName };
        placer.PlaceObjects(map, GridPositions(plan.ObjectCount));

        var heightMap = mapFile.HeightMapData;
        var center = new Vector3(WorldWidth / 2f, WorldWidth / 2f, 0f);
        var cellX = CellOf(center.X);
        var cellY = CellOf(center.Y);
        var centerBefore = (int)heightMap.Elevations[cellX, cellY];

        var brush = new HeightBrushTool { Mode = HeightBrushMode.Mound };
        var strokeEnd = center + new Vector3(2f * Spacing, 2f * Spacing, 0f);
        brush.OnPointerDown(PointerAt(center, ViewportPointerEventType.Down), map);
        brush.OnPointerMove(PointerAt(strokeEnd, ViewportPointerEventType.Move), map);
        brush.OnPointerUp(PointerAt(strokeEnd, ViewportPointerEventType.Up), map);

        return (map, (int)heightMap.Elevations[cellX, cellY] - centerBefore);
    }

    private static (int Moved, int Deleted, int CountAfterDelete, int CountAfterUndo, bool Restored) RegionEdit(
        EditorMap map,
        B4aConsumerPlan plan)
    {
        var objectsList = map.MapFile.ObjectsList;
        var authored = objectsList.Objects.ToArray();
        var authoredPositions = authored.ToDictionary<MapObject, MapObject, Vector3>(
            o => o,
            o => o.Position,
            ReferenceEqualityComparer.Instance);
        var moveDelta = new Vector3(0f, 2f * Spacing, 0f);

        var selector = new SelectTool();

        var (leftMin, leftMax) = StripRect(plan.ObjectCount, leftEdge: true);
        DragSelect(selector, map, leftMin, leftMax);
        var movedSet = selector.SelectedObjects.ToArray();
        selector.MoveSelectionBy(map, moveDelta);
        var moved = movedSet.Count(o => o.Position == authoredPositions[o] + moveDelta);

        var (rightMin, rightMax) = StripRect(plan.ObjectCount, leftEdge: false);
        DragSelect(selector, map, rightMin, rightMax);
        var deletedSet = selector.SelectedObjects.ToArray();
        selector.DeleteSelection(map);
        var remaining = new HashSet<MapObject>(objectsList.Objects, ReferenceEqualityComparer.Instance);
        var deleted = deletedSet.Count(o => !remaining.Contains(o));
        var countAfterDelete = objectsList.Objects.Length;

        map.UndoStack.Undo();
        map.UndoStack.Undo();
        var restored = objectsList.Objects.SequenceEqual(authored, ReferenceEqualityComparer.Instance)
            && authored.All(o => o.Position == authoredPositions[o]);

        return (moved, deleted, countAfterDelete, objectsList.Objects.Length, restored);
    }

    private static void DragSelect(SelectTool selector, EditorMap map, Vector3 min, Vector3 max)
    {
        selector.OnPointerDown(PointerAt(min, ViewportPointerEventType.Down), map);
        selector.OnPointerMove(PointerAt(max, ViewportPointerEventType.Move), map);
        selector.OnPointerUp(PointerAt(max, ViewportPointerEventType.Up), map);
    }

    private static ViewportPointerEvent PointerAt(Vector3 world, ViewportPointerEventType type)
    {
        return new ViewportPointerEvent
        {
            Position = Vector2.Zero,
            EventType = type,
            Button = ViewportMouseButton.Left,
            WorldPosition = world,
        };
    }

    private static bool LoadedMatchesSaved((string TypeName, Vector3 Position)[] saved, MapObject[] loaded)
    {
        return saved.Length == loaded.Length
            && saved.Zip(loaded, (s, l) => s.TypeName == l.TypeName && s.Position == l.Position).All(match => match);
    }

    private static ushort[,] BuildHeights()
    {
        var heights = new ushort[LatticeSize, LatticeSize];
        for (var y = 0; y < LatticeSize; y++)
        {
            var fy = y * 0.05f;
            for (var x = 0; x < LatticeSize; x++)
            {
                var fx = x * 0.05f;
                var h = 2500f + 300f * MathF.Sin(fx) * MathF.Cos(fy) + 100f * MathF.Sin(fx * 2.3f + fy * 1.7f);
                heights[x, y] = (ushort)Math.Clamp((int)h, 100, 60000);
            }
        }

        return heights;
    }

    // The grid spans the central half of the playable area, clear of the 25%/75% start waypoints
    // that CreateFromHeights adds, so region queries select only benchmark objects.
    private static (int Columns, int Rows, float OriginX, float OriginY) GridLayout(int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;
        return (columns, rows, (WorldWidth - columns * Spacing) / 2f, (WorldWidth - rows * Spacing) / 2f);
    }

    private static Vector3[] GridPositions(int count)
    {
        var (columns, _, originX, originY) = GridLayout(count);
        return [.. Enumerable.Range(0, count).Select(i => new Vector3(
            originX + (i % columns) * Spacing,
            originY + (i / columns) * Spacing,
            0f))];
    }

    private static (Vector3 Min, Vector3 Max) StripRect(int count, bool leftEdge)
    {
        var (columns, rows, originX, originY) = GridLayout(count);
        var width = Math.Max(1, columns / 4);
        var firstColumn = leftEdge ? 0 : columns - width;
        var lastColumn = firstColumn + width - 1;
        return (
            new Vector3(originX + firstColumn * Spacing - Spacing / 2f, originY - Spacing / 2f, 0f),
            new Vector3(originX + lastColumn * Spacing + Spacing / 2f, originY + (rows - 1) * Spacing + Spacing / 2f, 0f));
    }

    private static int CellOf(float world)
    {
        return (int)(world / HeightMap.HorizontalScale) + BorderWidth;
    }
}
