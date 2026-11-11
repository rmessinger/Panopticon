using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-B4a-05: blend layer edits and tile paint strokes are region deltas. A stroke pushes at most one
/// undo entry carrying pre-stroke values, and a stroke that changes no cell pushes nothing.
/// </summary>
public sealed class LayerRegionDeltaTests
{
    private const float HorizontalScale = 10f;
    private const ushort PaintIndex = 9;
    private const float MeshStrength = 0.5f;
    private const ushort MeshTarget = 2000;

    [Theory]
    [InlineData(BlendLayer.Tiles, 1, 1, 2, 2)]
    [InlineData(BlendLayer.Blends, 1, 1, 2, 2)]
    [InlineData(BlendLayer.Impassability, 1, 1, 2, 2)]
    [InlineData(BlendLayer.Buildability, 1, 1, 2, 2)]
    [InlineData(BlendLayer.Tiles, -2, 3, 5, 4)]
    [InlineData(BlendLayer.Blends, -2, 3, 5, 4)]
    [InlineData(BlendLayer.Impassability, -2, 3, 5, 4)]
    [InlineData(BlendLayer.Buildability, -2, 3, 5, 4)]
    public void BlendLayerEdit_PushUndoRedoAreExactOverRectangle(BlendLayer layer, int startX, int startY, int width, int height)
    {
        var blendTileData = BlendTileData.Create(6, 5);
        var rectangle = new Rect(startX, startY, width, height);
        WriteCells(blendTileData, layer, new Rect(0, 0, 6, 5), mutated: false);

        var stack = new UndoRedoStack<IMapEdit>();
        stack.Push(BlendLayerEdit.Capture(blendTileData, layer, startX, startY, width, height,
            () => WriteCells(blendTileData, layer, rectangle, mutated: true), "Test layer edit"));
        AssertCells(blendTileData, layer, rectangle, mutatedInside: true);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            stack.Undo();
            AssertCells(blendTileData, layer, rectangle, mutatedInside: false);

            stack.Redo();
            AssertCells(blendTileData, layer, rectangle, mutatedInside: true);
        }

        stack.Undo();
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void PaintSingleTileStroke_PaintsTouchedCells_PushesOneEntry_AndRedoReapplies()
    {
        var editorMap = NewMap(32);
        var tiles = editorMap.MapFile.BlendTileData.Tiles;
        var painted = new[] { (10, 10), (11, 10), (11, 12) };

        var tool = new PaintSingleTileTool { SelectedTileIndex = PaintIndex };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(editorMap, painted[0])), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(editorMap, painted[1])), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(editorMap, painted[2])), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, CellWorld(editorMap, painted[2])), editorMap);

        AssertTiles(tiles, painted, PaintIndex);

        var stack = editorMap.UndoStack;
        stack.Undo();
        AssertTiles(tiles, Array.Empty<(int X, int Y)>(), PaintIndex);
        Assert.False(stack.CanUndo);

        stack.Redo();
        AssertTiles(tiles, painted, PaintIndex);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void PaintSingleTileStroke_PaintingExistingIndex_PushesNoEdit()
    {
        var editorMap = NewMap(32);

        var tool = new PaintSingleTileTool { SelectedTileIndex = 0 };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(editorMap, (10, 10))), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(editorMap, (11, 10))), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, CellWorld(editorMap, (11, 10))), editorMap);

        Assert.False(editorMap.UndoStack.CanUndo);
    }

    [Fact]
    public void PaintSingleTileStroke_OffGrid_ChangesNothingAndPushesNoEdit()
    {
        var editorMap = NewMap(32);
        var tiles = editorMap.MapFile.BlendTileData.Tiles;
        var far = new Vector3(-100000f, -100000f, 0f);

        var tool = new PaintSingleTileTool { SelectedTileIndex = PaintIndex };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, far), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, far), editorMap);

        AssertTiles(tiles, Array.Empty<(int X, int Y)>(), PaintIndex);
        Assert.False(editorMap.UndoStack.CanUndo);
    }

    [Fact]
    public void PaintLargeTileStroke_OneEntryCoversItsCells_AndUndoRestores()
    {
        var editorMap = NewMap(32);
        var tiles = editorMap.MapFile.BlendTileData.Tiles;
        var block = new[] { (10, 10), (11, 10), (10, 11), (11, 11) };

        var tool = new PaintLargeTileTool { SelectedTileIndex = PaintIndex, TileSize = 2 };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(editorMap, (10, 10))), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, CellWorld(editorMap, (10, 10))), editorMap);

        AssertTiles(tiles, block, PaintIndex);

        var stack = editorMap.UndoStack;
        stack.Undo();
        AssertTiles(tiles, Array.Empty<(int X, int Y)>(), PaintIndex);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void PaintLargeTileDrag_OverlappingPositions_PushesOneEntry_AndUndoRestoresPreStroke()
    {
        var editorMap = NewMap(32);
        var tiles = editorMap.MapFile.BlendTileData.Tiles;
        var covered = new[] { (10, 10), (11, 10), (12, 10), (10, 11), (11, 11), (12, 11) };

        var tool = new PaintLargeTileTool { SelectedTileIndex = PaintIndex, TileSize = 2 };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(editorMap, (10, 10))), editorMap);
        tool.OnPointerMove(Event(ViewportPointerEventType.Move, CellWorld(editorMap, (11, 10))), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, CellWorld(editorMap, (11, 10))), editorMap);

        AssertTiles(tiles, covered, PaintIndex);

        var stack = editorMap.UndoStack;
        stack.Undo();
        AssertTiles(tiles, Array.Empty<(int X, int Y)>(), PaintIndex);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void SpillPaintFill_OnUniformGrid_PushesOneEntry_AndUndoRestoresFill()
    {
        var editorMap = NewMap(8);
        var tiles = editorMap.MapFile.BlendTileData.Tiles;
        const ushort oddTile = 5;
        var odd = (x: 10, y: 10);
        tiles[odd.x, odd.y] = oddTile;
        var preFill = (ushort[,])tiles.Clone();

        var tool = new SpillPaintTool { SelectedTileIndex = PaintIndex };
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, CellWorld(editorMap, (7, 7))), editorMap);

        for (var y = 0; y < tiles.GetLength(1); y++)
        {
            for (var x = 0; x < tiles.GetLength(0); x++)
            {
                Assert.Equal((x, y) == odd ? oddTile : PaintIndex, tiles[x, y]);
            }
        }

        var stack = editorMap.UndoStack;
        stack.Undo();
        Assert.Equal(preFill.Cast<ushort>(), tiles.Cast<ushort>());
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void MeshMoldStamp_MovesCentreOnceTowardTarget_AndUndoRestoresGrid()
    {
        var editorMap = NewPatternedMap(32);
        var elevations = editorMap.MapFile.HeightMapData!.Elevations;
        var centre = (x: elevations.GetLength(0) / 2, y: elevations.GetLength(1) / 2);
        var preStamp = (ushort[,])elevations.Clone();
        var centreBefore = elevations[centre.x, centre.y];

        var tool = new MeshMoldTool
        {
            TargetElevation = MeshTarget,
            Brush = { RadiusCells = 3, Strength = MeshStrength },
        };
        var centreWorld = CellWorld(editorMap, centre);
        tool.OnPointerDown(Event(ViewportPointerEventType.Down, centreWorld), editorMap);
        tool.OnPointerUp(Event(ViewportPointerEventType.Up, centreWorld), editorMap);

        const float t = MeshStrength;
        Assert.Equal((ushort)(centreBefore * (1 - t) + MeshTarget * t), elevations[centre.x, centre.y]);

        var stack = editorMap.UndoStack;
        stack.Undo();
        Assert.Equal(preStamp.Cast<ushort>(), elevations.Cast<ushort>());
        Assert.False(stack.CanUndo);
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    private static Array LayerOf(BlendTileData blendTileData, BlendLayer layer) => layer switch
    {
        BlendLayer.Tiles => blendTileData.Tiles,
        BlendLayer.Blends => blendTileData.Blends,
        BlendLayer.Impassability => blendTileData.Impassability,
        _ => blendTileData.Buildability,
    };

    private static object CellValue(BlendLayer layer, int x, int y, bool mutated) => layer switch
    {
        BlendLayer.Tiles => mutated ? (ushort)(900 + x) : (ushort)(x * 3 + y + 1),
        BlendLayer.Blends => mutated ? (uint)(5000 + y) : (uint)(x * 7 + y + 100),
        _ => mutated ? (x + y) % 2 != 0 : (x + y) % 2 == 0,
    };

    private static void WriteCells(BlendTileData blendTileData, BlendLayer layer, Rect rectangle, bool mutated)
    {
        var cells = LayerOf(blendTileData, layer);
        for (var y = rectangle.Y; y < rectangle.Y + rectangle.Height; y++)
        {
            for (var x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
            {
                if (x >= 0 && y >= 0 && x < cells.GetLength(0) && y < cells.GetLength(1))
                {
                    cells.SetValue(CellValue(layer, x, y, mutated), x, y);
                }
            }
        }
    }

    private static void AssertCells(BlendTileData blendTileData, BlendLayer layer, Rect rectangle, bool mutatedInside)
    {
        var cells = LayerOf(blendTileData, layer);
        for (var y = 0; y < cells.GetLength(1); y++)
        {
            for (var x = 0; x < cells.GetLength(0); x++)
            {
                var mutated = mutatedInside && rectangle.Contains(x, y);
                Assert.Equal(CellValue(layer, x, y, mutated), cells.GetValue(x, y));
            }
        }
    }

    private static void AssertTiles(ushort[,] tiles, IEnumerable<(int X, int Y)> painted, ushort paintIndex)
    {
        var paintedCells = painted.ToHashSet();
        for (var y = 0; y < tiles.GetLength(1); y++)
        {
            for (var x = 0; x < tiles.GetLength(0); x++)
            {
                Assert.Equal(paintedCells.Contains((x, y)) ? paintIndex : (ushort)0, tiles[x, y]);
            }
        }
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

    private static Vector3 CellWorld(EditorMap editorMap, (int x, int y) cell)
    {
        var borderWidth = (int)editorMap.MapFile.HeightMapData!.BorderWidth;
        return new Vector3(
            (cell.x - borderWidth) * HorizontalScale + 5f,
            (cell.y - borderWidth) * HorizontalScale + 5f,
            0f);
    }

    private static ViewportPointerEvent Event(ViewportPointerEventType eventType, Vector3 worldPosition) => new()
    {
        Position = new Vector2(worldPosition.X, worldPosition.Y),
        EventType = eventType,
        Button = ViewportMouseButton.Left,
        WorldPosition = worldPosition,
    };
}
