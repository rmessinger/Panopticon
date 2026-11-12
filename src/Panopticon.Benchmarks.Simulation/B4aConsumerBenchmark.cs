using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// B4a consumer measurements for the WorldBuilder editing pipeline. Each repeat runs five stages on
/// a fresh map: author (bulk placement, one height-brush stroke, and one Mesh Mold stroke on a map
/// built from heights), region edit (select, move, delete, undo), save (directory package), load
/// (package reopen), and play (<see cref="IB4aPlayRuntime"/>). Each stage invocation is one B1 tick
/// bracket and one named system span. The Mesh Mold undo and redo check runs after the author stage,
/// outside its bracket. No install is required when the play runtime is install-free.
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

    private const float Spacing = 20f;
    private const string ObjectTypeName = "AmericaTankCrusader";
    private const string PlacementIndexProperty = "b4aPlacementIndex";

    public static B4aConsumerRun Run(B4aConsumerPlan plan, IB4aPlayRuntime play, Action<string>? stageStarting = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(play);
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.Repeats, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(plan.ObjectCount, 16);

        var root = Path.Combine(Path.GetTempPath(), $"b4a-consumer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var meter = new B4aStageMeter { StageStarting = stageStarting };
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
        var (map, brushCenterDelta, moldStroke) = meter.Measure(AuthorStage, () => Author(plan));
        var authoredCount = map.MapFile.ObjectsList.Objects.Length;
        var (moldEntriesAdded, moldChangedCells, moldUndoRestored, moldRedoReapplied) = VerifyMeshMoldUndo(map, moldStroke);

        var region = meter.Measure(RegionEditStage, () => RegionEdit(map, plan));

        var savedObjects = map.MapFile.ObjectsList.Objects.Select(PersistedIdentity).ToArray();
        var savedPropertyCount = map.MapFile.ObjectsList.Objects.Sum(o => o.Properties.Count);
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
            played,
            moldEntriesAdded,
            moldChangedCells,
            moldUndoRestored,
            moldRedoReapplied,
            savedPropertyCount);
    }

    private static (EditorMap Map, int BrushCenterDelta, MeshMoldStroke MoldStroke) Author(B4aConsumerPlan plan)
    {
        var worldWidth = WorldWidth(plan);
        var mapFile = MapFile.CreateFromHeights(BuildHeights(plan), B4aConsumerPlan.BorderWidth);
        var map = EditorMap.CreateNew(mapFile);

        var placer = new PlaceObjectTool { SelectedObjectType = ObjectTypeName };
        placer.PlaceObjects(map, GridPositions(plan.ObjectCount, worldWidth));

        var objects = mapFile.ObjectsList.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            objects[index].Properties.AddInteger(PlacementIndexProperty, index);
        }

        var heightMap = mapFile.HeightMapData;
        var center = new Vector3(worldWidth / 2f, worldWidth / 2f, 0f);
        var cellX = CellOf(center.X);
        var cellY = CellOf(center.Y);
        var centerBefore = (int)heightMap.Elevations[cellX, cellY];

        var brush = new HeightBrushTool { Mode = HeightBrushMode.Mound };
        var strokeEnd = center + new Vector3(2f * Spacing, 2f * Spacing, 0f);
        brush.OnPointerDown(PointerAt(center, ViewportPointerEventType.Down), map);
        brush.OnPointerMove(PointerAt(strokeEnd, ViewportPointerEventType.Move), map);
        brush.OnPointerUp(PointerAt(strokeEnd, ViewportPointerEventType.Up), map);
        var brushCenterDelta = (int)heightMap.Elevations[cellX, cellY] - centerBefore;

        // Offset clear of the height-brush footprint so each undo reverts only its own stroke.
        var moldStart = center + new Vector3(-10f * Spacing, -10f * Spacing, 0f);
        var moldStroke = StrokeMeshMold(map, moldStart, moldStart + new Vector3(2f * Spacing, 0f, 0f));

        return (map, brushCenterDelta, moldStroke);
    }

    private static MeshMoldStroke StrokeMeshMold(EditorMap map, Vector3 start, Vector3 end)
    {
        var tool = new MeshMoldTool();
        var elevations = map.MapFile.HeightMapData.Elevations;
        var (minX, minY, width, height) = StrokeBounds(elevations, tool.Brush.RadiusCells, start, end);
        var before = ReadRegion(elevations, minX, minY, width, height);

        var pushes = 0;
        EventHandler countPush = (_, _) => pushes++;
        map.UndoStack.StateChanged += countPush;
        tool.OnPointerDown(PointerAt(start, ViewportPointerEventType.Down), map);
        tool.OnPointerMove(PointerAt(end, ViewportPointerEventType.Move), map);
        tool.OnPointerUp(PointerAt(end, ViewportPointerEventType.Up), map);
        map.UndoStack.StateChanged -= countPush;

        var after = ReadRegion(elevations, minX, minY, width, height);
        return new MeshMoldStroke(minX, minY, before, after, pushes);
    }

    private static (int EntriesAdded, int ChangedCellCount, bool UndoRestoredPreStroke, bool RedoReappliedStroke) VerifyMeshMoldUndo(
        EditorMap map,
        MeshMoldStroke stroke)
    {
        var elevations = map.MapFile.HeightMapData.Elevations;
        var width = stroke.Before.GetLength(0);
        var height = stroke.Before.GetLength(1);

        map.UndoStack.Undo();
        var undoRestored = ReadRegion(elevations, stroke.MinX, stroke.MinY, width, height)
            .Cast<ushort>()
            .SequenceEqual(stroke.Before.Cast<ushort>());

        map.UndoStack.Redo();
        var redoReapplied = ReadRegion(elevations, stroke.MinX, stroke.MinY, width, height)
            .Cast<ushort>()
            .SequenceEqual(stroke.After.Cast<ushort>());

        var changedCells = stroke.Before.Cast<ushort>().Zip(stroke.After.Cast<ushort>()).Count(p => p.First != p.Second);
        return (stroke.UndoEntriesAdded, changedCells, undoRestored, redoReapplied);
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
        var worldWidth = WorldWidth(plan);

        var selector = new SelectTool();

        var (leftMin, leftMax) = StripRect(plan.ObjectCount, worldWidth, leftEdge: true);
        DragSelect(selector, map, leftMin, leftMax);
        var movedSet = selector.SelectedObjects.ToArray();
        selector.MoveSelectionBy(map, moveDelta);
        var moved = movedSet.Count(o => o.Position == authoredPositions[o] + moveDelta);

        var (rightMin, rightMax) = StripRect(plan.ObjectCount, worldWidth, leftEdge: false);
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

    private static bool LoadedMatchesSaved(string[] saved, MapObject[] loaded)
    {
        return saved.SequenceEqual(loaded.Select(PersistedIdentity));
    }

    // Covers every field the map file persists, so equal strings mean equal saved state.
    // Properties sort by name because the package does not preserve insertion order.
    private static string PersistedIdentity(MapObject mapObject)
    {
        return JsonSerializer.Serialize(new
        {
            mapObject.TypeName,
            mapObject.Position.X,
            mapObject.Position.Y,
            mapObject.Position.Z,
            mapObject.Angle,
            RoadType = (uint)mapObject.RoadType,
            Properties = mapObject.Properties.OrderBy(p => p.Key.Name, StringComparer.Ordinal).Select(p => new
            {
                p.Key.Name,
                PropertyType = (byte)p.Key.PropertyType,
                Value = Convert.ToString(p.Value, CultureInfo.InvariantCulture),
            }),
        });
    }

    private static ushort[,] BuildHeights(B4aConsumerPlan plan)
    {
        var lattice = plan.LatticeSize;
        var heights = new ushort[lattice, lattice];
        for (var y = 0; y < lattice; y++)
        {
            var fy = y * 0.05f;
            for (var x = 0; x < lattice; x++)
            {
                var fx = x * 0.05f;
                var h = 2500f + 300f * MathF.Sin(fx) * MathF.Cos(fy) + 100f * MathF.Sin(fx * 2.3f + fy * 1.7f);
                heights[x, y] = (ushort)Math.Clamp((int)h, 100, 60000);
            }
        }

        return heights;
    }

    private static float WorldWidth(B4aConsumerPlan plan)
    {
        return plan.PlayableSize * HeightMap.HorizontalScale;
    }

    // The grid spans the central half of the playable area, clear of the 25%/75% start waypoints
    // that CreateFromHeights adds, so region queries select only benchmark objects.
    private static (int Columns, int Rows, float OriginX, float OriginY) GridLayout(int count, float worldWidth)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (count + columns - 1) / columns;
        return (columns, rows, (worldWidth - columns * Spacing) / 2f, (worldWidth - rows * Spacing) / 2f);
    }

    private static Vector3[] GridPositions(int count, float worldWidth)
    {
        var (columns, _, originX, originY) = GridLayout(count, worldWidth);
        return [.. Enumerable.Range(0, count).Select(i => new Vector3(
            originX + (i % columns) * Spacing,
            originY + (i / columns) * Spacing,
            0f))];
    }

    private static (Vector3 Min, Vector3 Max) StripRect(int count, float worldWidth, bool leftEdge)
    {
        var (columns, rows, originX, originY) = GridLayout(count, worldWidth);
        var width = Math.Max(1, columns / 4);
        var firstColumn = leftEdge ? 0 : columns - width;
        var lastColumn = firstColumn + width - 1;
        return (
            new Vector3(originX + firstColumn * Spacing - Spacing / 2f, originY - Spacing / 2f, 0f),
            new Vector3(originX + lastColumn * Spacing + Spacing / 2f, originY + (rows - 1) * Spacing + Spacing / 2f, 0f));
    }

    private static (int MinX, int MinY, int Width, int Height) StrokeBounds(
        ushort[,] elevations,
        int radius,
        Vector3 start,
        Vector3 end)
    {
        var startX = CellOf(start.X);
        var startY = CellOf(start.Y);
        var endX = CellOf(end.X);
        var endY = CellOf(end.Y);
        var minX = Math.Max(0, Math.Min(startX, endX) - radius);
        var minY = Math.Max(0, Math.Min(startY, endY) - radius);
        var maxX = Math.Min(elevations.GetLength(0) - 1, Math.Max(startX, endX) + radius);
        var maxY = Math.Min(elevations.GetLength(1) - 1, Math.Max(startY, endY) + radius);
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static ushort[,] ReadRegion(ushort[,] elevations, int minX, int minY, int width, int height)
    {
        var region = new ushort[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                region[x, y] = elevations[minX + x, minY + y];
            }
        }

        return region;
    }

    private static int CellOf(float world)
    {
        return (int)(world / HeightMap.HorizontalScale) + B4aConsumerPlan.BorderWidth;
    }

    private sealed record MeshMoldStroke(int MinX, int MinY, ushort[,] Before, ushort[,] After, int UndoEntriesAdded);
}
