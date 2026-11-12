using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WP1016: a mesh mold pointer stroke is a region delta. One stroke (down, moves, up) pushes exactly one
/// undo entry, undo and redo are exact, and stroke allocation scales with the stroke rectangle rather than the grid.
/// </summary>
public sealed class MeshMoldRegionDeltaTests
{
    private const float HorizontalScale = 10f;
    private const float Strength = 0.5f;
    private const ushort Target = 100;
    private const int Radius = 3;
    private const int MiB = 1024 * 1024;
    private const string MapPath = "mesh_mold_region_delta.map";

    private readonly ITestOutputHelper _output;

    public MeshMoldRegionDeltaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Stroke_PushesOneEntry_UndoRestoresPreStroke_AndRedoReappliesPostStroke()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var centre = CentreCell(grid);
        var tool = NewTool(Radius);

        Stroke(tool, editorMap, [centre, (centre.x + 2, centre.y), (centre.x + 4, centre.y + 1)], 5f);

        var postStroke = Snapshot(grid);
        Assert.False(preStroke.Cast<ushort>().SequenceEqual(postStroke.Cast<ushort>()));
        Assert.True(editorMap.IsDirty);
        Assert.True(editorMap.UndoStack.CanUndo);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);

        editorMap.UndoStack.Redo();
        AssertSameGrid(postStroke, grid);
        Assert.False(editorMap.UndoStack.CanRedo);
    }

    [Fact]
    public void Stroke_MatchesStampFormulaAppliedInOrder_IncludingOverlaps()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var expected = Snapshot(grid);
        var centre = CentreCell(grid);
        var cells = new[] { centre, (centre.x + 2, centre.y), (centre.x + 4, centre.y + 1), (centre.x + 4, centre.y + 1) };
        var worlds = cells.Select(cell => CellWorld(editorMap, cell, 5f)).ToArray();
        var preStroke = Snapshot(grid);
        var tool = NewTool(Radius);
        var border = (int)editorMap.MapFile.HeightMapData!.BorderWidth;

        foreach (var world in worlds)
        {
            ApplyStampReference(expected, world, Radius, border);
        }

        Stroke(tool, editorMap, cells, 5f);

        AssertSameGrid(expected, grid);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);
    }

    [Fact]
    public void CornerStroke_PartiallyClipped_ChangesInBoundsCellsInOneEntry()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var expected = Snapshot(grid);
        var cells = new[] { (x: -2, y: -1), (x: 0, y: 0), (x: 1, y: 1) };
        var border = (int)editorMap.MapFile.HeightMapData!.BorderWidth;
        foreach (var cell in cells)
        {
            ApplyStampReference(expected, CellWorld(editorMap, cell, 0f), Radius, border);
        }

        var tool = NewTool(Radius);
        Stroke(tool, editorMap, cells, 0f);

        AssertSameGrid(expected, grid);
        Assert.NotEqual(preStroke[0, 0], grid[0, 0]);
        Assert.True(editorMap.IsDirty);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);
    }

    [Fact]
    public void OffMapStroke_ChangesNothing_PushesNothing_AndDoesNotDirty()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var tool = NewTool(Radius);

        Stroke(tool, editorMap, [(-1000, -1000), (-1000, 1000), (1000, 1000)], 5f);

        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.UndoStack.CanRedo);
        Assert.False(editorMap.IsDirty);
    }

    [Fact]
    public void StrokeEntirelyBeyondEdges_ChangesNothing_PushesNothing_AndDoesNotDirty()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var tool = NewTool(Radius);

        Stroke(tool, editorMap, [(-Radius - 1, 10), (-Radius - 1, 11), (-Radius - 2, 12)], 0f);
        Stroke(tool, editorMap, [(grid.GetLength(0) + Radius, 10), (grid.GetLength(0) + Radius, 20)], 0f);

        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.IsDirty);
    }

    [Fact]
    public void StampWritingNoCell_PushesNothing_AndDoesNotDirty()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var tool = NewTool(Radius);

        // Footprint reaches cell (0, 0) only at its corner, which lies outside the circle of radius Radius.
        Stroke(tool, editorMap, [(-Radius, -Radius)], 0f);

        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.IsDirty);
    }

    [Fact]
    public void StrokeEnteringFromOffMap_PushesOneEntry_AndUndoRestoresPreStroke()
    {
        var editorMap = NewPatternedMap(32);
        var grid = Grid(editorMap);
        var preStroke = Snapshot(grid);
        var centre = CentreCell(grid);
        var tool = NewTool(Radius);

        Stroke(tool, editorMap, [(-500, -500), centre, (centre.x + 1, centre.y)], 5f);
        var postStroke = Snapshot(grid);
        Assert.False(preStroke.Cast<ushort>().SequenceEqual(postStroke.Cast<ushort>()));

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, grid);
        Assert.False(editorMap.UndoStack.CanUndo);

        editorMap.UndoStack.Redo();
        AssertSameGrid(postStroke, grid);
    }

    [Fact]
    public void RadiusTenStroke_AllocationScalesWithStrokeRectangle()
    {
        var small = MeasureStrokeAllocation(128);
        var large = MeasureStrokeAllocation(4096);

        _output.WriteLine($"Radius-10 stroke allocated bytes: playable 128 = {small}, playable 4096 = {large}");

        Assert.True(large < 8 * MiB, $"4096 stroke allocated {large} bytes, expected under {8 * MiB}.");
        Assert.True(large < 4 * small + MiB, $"4096 stroke allocated {large} bytes, expected under 4 x {small} + {MiB}.");
    }

    private static long MeasureStrokeAllocation(int playable)
    {
        var editorMap = NewMap(playable);
        var centre = CellWorld(editorMap, CentreCell(Grid(editorMap)), 5f);
        var tool = NewTool(10);

        tool.OnPointerDown(Event(ViewportPointerEventType.Down, centre), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, centre), editorMap);

        var down = Event(ViewportPointerEventType.Down, centre);
        var up = Event(ViewportPointerEventType.Up, centre);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        tool.OnPointerDown(down, editorMap);
        tool.OnPointerUp(up, editorMap);
        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static void ApplyStampReference(ushort[,] grid, Vector3 world, int radius, int border)
    {
        var cx = (int)(world.X / HorizontalScale) + border;
        var cy = (int)(world.Y / HorizontalScale) + border;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                var x = cx + dx;
                var y = cy + dy;
                if (distance > radius || x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1))
                {
                    continue;
                }

                var t = Strength * (1.0f - distance / radius);
                grid[x, y] = (ushort)(grid[x, y] * (1 - t) + Target * t);
            }
        }
    }

    private static void Stroke(MeshMoldTool tool, EditorMap editorMap, (int x, int y)[] cells, float offset)
    {
        var worlds = cells.Select(cell => CellWorld(editorMap, cell, offset)).ToArray();
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, worlds[0]), editorMap);
        foreach (var world in worlds.Skip(1))
        {
            tool.OnPointerMove(Event(ViewportPointerEventType.Move, world), editorMap);
        }

        tool.OnPointerUp(Event(ViewportPointerEventType.Up, worlds[^1]), editorMap);
    }

    private static EditorMap NewMap(int playable) => EditorMap.Open(NewMapFactory.Create(new NewMapParameters
    {
        PlayableWidth = playable,
        PlayableHeight = playable,
        InitialHeight = 10,
        BorderWidth = 6,
    }).MapFile, MapPath);

    private static EditorMap NewPatternedMap(int playable)
    {
        var editorMap = NewMap(playable);
        var elevations = Grid(editorMap);
        for (var y = 0; y < elevations.GetLength(1); y++)
        {
            for (var x = 0; x < elevations.GetLength(0); x++)
            {
                elevations[x, y] = (ushort)(20 + (x * 37 + y * 53) % 160);
            }
        }

        return editorMap;
    }

    private static MeshMoldTool NewTool(int radius) => new()
    {
        TargetElevation = Target,
        Brush = { RadiusCells = radius, Strength = Strength },
    };

    private static ushort[,] Grid(EditorMap editorMap) => editorMap.MapFile.HeightMapData!.Elevations;

    private static (int x, int y) CentreCell(ushort[,] grid) =>
        (grid.GetLength(0) / 2, grid.GetLength(1) / 2);

    private static Vector3 CellWorld(EditorMap editorMap, (int x, int y) cell, float offset)
    {
        var borderWidth = (int)editorMap.MapFile.HeightMapData!.BorderWidth;
        return new Vector3(
            (cell.x - borderWidth) * HorizontalScale + offset,
            (cell.y - borderWidth) * HorizontalScale + offset,
            0f);
    }

    private static ushort[,] Snapshot(ushort[,] grid) => (ushort[,])grid.Clone();

    private static void AssertSameGrid(ushort[,] expected, ushort[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.Equal(expected.Cast<ushort>(), actual.Cast<ushort>());
    }

    private static ViewportPointerEvent Event(ViewportPointerEventType eventType, Vector3 worldPosition) => new()
    {
        Position = new Vector2(worldPosition.X, worldPosition.Y),
        EventType = eventType,
        Button = ViewportMouseButton.Left,
        WorldPosition = worldPosition,
    };
}
