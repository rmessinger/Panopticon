using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// Region-delta semantics that hold today (WI-B4a-03): Push writes the after arrays, Undo and Redo
/// restore them exactly, writes are clipped to the grid, and LIFO undo unwinds overlapping edits.
/// </summary>
public sealed class RegionDeltaContractTests
{
    [Fact]
    public void HeightCapturePushWritesAfterStateInsideRectangleOnly()
    {
        var heightMapData = HeightMapData.Create(0, Baseline());
        var edit = CaptureThenRestore(heightMapData, 1, 1, 2, 2, e => Add(e, 1, 1, 2, 2, 100));

        var stack = new UndoRedoStack<IMapEdit>();
        stack.Push(edit);

        Assert.Equal(Add(Baseline(), 1, 1, 2, 2, 100).Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());
    }

    [Fact]
    public void HeightUndoRedoRestoresExactBeforeAndAfterOverCycles()
    {
        var heightMapData = HeightMapData.Create(0, Baseline());
        var edit = CaptureThenRestore(heightMapData, 1, 1, 2, 2, e => Add(e, 1, 1, 2, 2, 100));
        var stack = new UndoRedoStack<IMapEdit>();
        stack.Push(edit);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            stack.Undo();
            Assert.Equal(Baseline().Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());

            stack.Redo();
            Assert.Equal(Add(Baseline(), 1, 1, 2, 2, 100).Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());
        }
    }

    [Fact]
    public void HeightEditPartlyOutsideGridAppliesAndRevertsInBoundsCellsOnly()
    {
        var heightMapData = HeightMapData.Create(0, Baseline());
        var edit = CaptureThenRestore(heightMapData, -1, 2, 3, 3, e => Add(e, -1, 2, 3, 3, 500));
        var stack = new UndoRedoStack<IMapEdit>();
        stack.Push(edit);

        Assert.Equal(Add(Baseline(), -1, 2, 3, 3, 500).Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());

        stack.Undo();
        Assert.Equal(Baseline().Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());

        stack.Redo();
        Assert.Equal(Add(Baseline(), -1, 2, 3, 3, 500).Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());
    }

    [Fact]
    public void BlendSingleCellEditCyclesOneCellExactly()
    {
        var blendTileData = BlendTileData.Create(4, 4);
        var stack = new UndoRedoStack<IMapEdit>();
        stack.Push(BlendTileEdit.CreateSingle(blendTileData, 2, 1, 77));

        for (var cycle = 0; cycle < 3; cycle++)
        {
            stack.Undo();
            Assert.Equal(0, blendTileData.Tiles[2, 1]);
            Assert.Equal(0, blendTileData.Tiles.Cast<ushort>().Count(tile => tile != 0));

            stack.Redo();
            Assert.Equal(77, blendTileData.Tiles[2, 1]);
            Assert.Equal(1, blendTileData.Tiles.Cast<ushort>().Count(tile => tile != 0));
        }
    }

    [Fact]
    public void OverlappingHeightEditsUndoInLifoOrderToOriginal()
    {
        var heightMapData = HeightMapData.Create(0, Baseline());
        var stack = new UndoRedoStack<IMapEdit>();

        stack.Push(HeightMapEdit.Capture(heightMapData, 0, 0, 3, 3, e => Add(e, 0, 0, 3, 3, 10), "first"));
        var afterFirst = (ushort[,])heightMapData.Elevations.Clone();
        stack.Push(HeightMapEdit.Capture(heightMapData, 1, 1, 3, 3, e => Add(e, 1, 1, 3, 3, 100), "second"));

        stack.Undo();
        Assert.Equal(afterFirst.Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());

        stack.Undo();
        Assert.Equal(Baseline().Cast<ushort>(), heightMapData.Elevations.Cast<ushort>());
        Assert.False(stack.CanUndo);
    }

    private static HeightMapEdit CaptureThenRestore(HeightMapData heightMapData, int startX, int startY,
        int width, int height, Action<ushort[,]> action)
    {
        var preStroke = (ushort[,])heightMapData.Elevations.Clone();
        var edit = HeightMapEdit.Capture(heightMapData, startX, startY, width, height, action, "region");
        Array.Copy(preStroke, heightMapData.Elevations, preStroke.Length);
        return edit;
    }

    private static ushort[,] Baseline()
    {
        var elevations = new ushort[4, 4];
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                elevations[x, y] = (ushort)(x * 10 + y + 1);
            }
        }

        return elevations;
    }

    private static ushort[,] Add(ushort[,] elevations, int startX, int startY, int width, int height, int delta)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var x = startX + dx;
                var y = startY + dy;
                if (x >= 0 && y >= 0 && x < elevations.GetLength(0) && y < elevations.GetLength(1))
                {
                    elevations[x, y] = (ushort)(elevations[x, y] + delta);
                }
            }
        }

        return elevations;
    }
}
