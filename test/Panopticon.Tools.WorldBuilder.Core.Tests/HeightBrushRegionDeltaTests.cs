using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-B4a-04: height brush strokes are region deltas (R1 to R13 of the region-delta contract).
/// Undo restores the pre-stroke grid, Redo reapplies the post-stroke grid, one stroke is one undo
/// entry, and stroke allocation scales with the stroke rectangle rather than the grid.
/// </summary>
public sealed class HeightBrushRegionDeltaTests
{
    private const float HorizontalScale = 10f;
    private const int DefaultRadius = 3;
    private const int MiB = 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public HeightBrushRegionDeltaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(HeightBrushMode.Mound)]
    [InlineData(HeightBrushMode.Dig)]
    [InlineData(HeightBrushMode.Smooth)]
    public void Stroke_UndoRestoresPreStrokeGrid_AndRedoReappliesPostStrokeGrid(HeightBrushMode mode)
    {
        var editorMap = NewPatternedMap(32);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var preStroke = Snapshot(heightMapData);
        var centre = CellWorld(heightMapData, CentreCell(heightMapData), 5f);

        var tool = NewTool(mode, DefaultRadius);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, centre), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, centre), editorMap);

        var postStroke = Snapshot(heightMapData);
        Assert.False(preStroke.Cast<ushort>().SequenceEqual(postStroke.Cast<ushort>()));

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, heightMapData.Elevations);

        editorMap.UndoStack.Redo();
        AssertSameGrid(postStroke, heightMapData.Elevations);
    }

    [Fact]
    public void MultiMoveStroke_PushesExactlyOneUndoEntry()
    {
        var editorMap = NewPatternedMap(32);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var preStroke = Snapshot(heightMapData);
        var centreCell = CentreCell(heightMapData);

        var tool = NewTool(HeightBrushMode.Mound, DefaultRadius);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(heightMapData, centreCell, 5f)), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(heightMapData, (centreCell.x + 2, centreCell.y), 5f)), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(heightMapData, (centreCell.x + 4, centreCell.y + 1), 5f)), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, CellWorld(heightMapData, (centreCell.x + 4, centreCell.y + 1), 5f)), editorMap);

        Assert.False(preStroke.Cast<ushort>().SequenceEqual(heightMapData.Elevations.Cast<ushort>()));
        Assert.True(editorMap.UndoStack.CanUndo);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, heightMapData.Elevations);
        Assert.False(editorMap.UndoStack.CanUndo);
    }

    [Fact]
    public void StrokeCentredOnMapEdge_UndoAndRedoAreExact()
    {
        var editorMap = NewPatternedMap(32);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var preStroke = Snapshot(heightMapData);
        var edgeCell = (x: 0, y: CentreCell(heightMapData).y);
        var edge = CellWorld(heightMapData, edgeCell, 0f);

        var tool = NewTool(HeightBrushMode.Mound, DefaultRadius);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, edge), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, edge), editorMap);

        Assert.Equal(preStroke[edgeCell.x, edgeCell.y] + 25, heightMapData.Elevations[edgeCell.x, edgeCell.y]);
        var postStroke = Snapshot(heightMapData);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, heightMapData.Elevations);

        editorMap.UndoStack.Redo();
        AssertSameGrid(postStroke, heightMapData.Elevations);
    }

    [Fact]
    public void OffMapStroke_ChangesNothingAndPushesNoEdit()
    {
        var editorMap = NewPatternedMap(32);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var preStroke = Snapshot(heightMapData);

        var farNegative = new Vector3(-100000f, -100000f, 0f);
        var farPositive = new Vector3(100000f, 100000f, 0f);
        var tool = NewTool(HeightBrushMode.Mound, DefaultRadius);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, farNegative), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, farPositive), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, farPositive), editorMap);

        AssertSameGrid(preStroke, heightMapData.Elevations);
        Assert.False(editorMap.UndoStack.CanUndo);
        Assert.False(editorMap.UndoStack.CanRedo);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void StrokeAtSubCellOffset_EditsAndRestoresSameCell(float offset)
    {
        var editorMap = NewPatternedMap(32);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var preStroke = Snapshot(heightMapData);
        var centreCell = CentreCell(heightMapData);
        var world = CellWorld(heightMapData, centreCell, offset);

        var tool = NewTool(HeightBrushMode.Mound, DefaultRadius);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, world), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, world), editorMap);

        Assert.Equal(preStroke[centreCell.x, centreCell.y] + 25, heightMapData.Elevations[centreCell.x, centreCell.y]);
        var postStroke = Snapshot(heightMapData);

        editorMap.UndoStack.Undo();
        AssertSameGrid(preStroke, heightMapData.Elevations);

        editorMap.UndoStack.Redo();
        AssertSameGrid(postStroke, heightMapData.Elevations);
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

    private long MeasureStrokeAllocation(int playable)
    {
        var editorMap = NewMap(playable);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var centre = CellWorld(heightMapData, CentreCell(heightMapData), 5f);
        var tool = NewTool(HeightBrushMode.Mound, 10);

        tool.OnPointerDown(Event(ViewportPointerEventType.Down, centre), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, centre), editorMap);

        var down = Event(ViewportPointerEventType.Down, centre);
        var up = Event(ViewportPointerEventType.Up, centre);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        tool.OnPointerDown(down, editorMap);
        tool.OnPointerUp(up, editorMap);
        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static EditorMap NewMap(int playable) => NewMapFactory.Create(new NewMapParameters
    {
        PlayableWidth = playable,
        PlayableHeight = playable,
        InitialHeight = 10,
        BorderWidth = 6,
    });

    private static EditorMap NewPatternedMap(int playable)
    {
        var editorMap = NewMap(playable);
        var elevations = editorMap.MapFile.HeightMapData!.Elevations;
        for (var y = 0; y < elevations.GetLength(1); y++)
        {
            for (var x = 0; x < elevations.GetLength(0); x++)
            {
                elevations[x, y] = (ushort)(20 + (x * 37 + y * 53) % 160);
            }
        }

        return editorMap;
    }

    private static HeightBrushTool NewTool(HeightBrushMode mode, int radius) => new()
    {
        Mode = mode,
        Brush = { RadiusCells = radius, Strength = 1f, HeightDelta = 25 },
    };

    private static (int x, int y) CentreCell(HeightMapData heightMapData) =>
        (heightMapData.Elevations.GetLength(0) / 2, heightMapData.Elevations.GetLength(1) / 2);

    private static Vector3 CellWorld(HeightMapData heightMapData, (int x, int y) cell, float offset) => new(
        (cell.x - (int)heightMapData.BorderWidth) * HorizontalScale + offset,
        (cell.y - (int)heightMapData.BorderWidth) * HorizontalScale + offset,
        0f);

    private static ushort[,] Snapshot(HeightMapData heightMapData) => (ushort[,])heightMapData.Elevations.Clone();

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
