using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-A5-27: the undo stack's Push is the single application owner for map edits. Factories
/// build edits without applying them, so one placement adds exactly one object, and undo/redo
/// cycles stay exact for placement, deletion, blend, and height edits.
/// </summary>
public sealed class UndoRedoEditApplicationTests
{
    private static MapObject NewObject(float x) => new(new Vector3(x, 0, 0), 0f, RoadType.None, "TestObject");

    [Fact]
    public void PlacementPushAddsExactlyOneObject_AndUndoRedoCycles()
    {
        var objectsList = new ObjectsList();
        var stack = new UndoRedoStack<IMapEdit>();
        var mapObject = NewObject(1);

        stack.Push(ObjectPlacementEdit.CreatePlacement(objectsList, mapObject));

        var placed = Assert.Single(objectsList.Objects);
        Assert.Same(mapObject, placed);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            stack.Undo();
            Assert.False(stack.CanUndo);
            Assert.Empty(objectsList.Objects);

            stack.Redo();
            Assert.False(stack.CanRedo);
            placed = Assert.Single(objectsList.Objects);
            Assert.Same(mapObject, placed);
        }
    }

    [Fact]
    public void DeletionPushRemovesExactlyOnce_AndUndoRestores()
    {
        var objectsList = new ObjectsList();
        var stack = new UndoRedoStack<IMapEdit>();
        var first = NewObject(1);
        var second = NewObject(2);
        objectsList.AddObject(first);
        objectsList.AddObject(second);

        stack.Push(ObjectPlacementEdit.CreateDeletion(objectsList, second));

        var remaining = Assert.Single(objectsList.Objects);
        Assert.Same(first, remaining);

        stack.Undo();
        Assert.Equal(2, objectsList.Objects.Length);
        Assert.Contains(first, objectsList.Objects);
        Assert.Contains(second, objectsList.Objects);

        stack.Redo();
        remaining = Assert.Single(objectsList.Objects);
        Assert.Same(first, remaining);
    }

    [Fact]
    public void BlendEditPushAppliesOnce_AndReverts()
    {
        var blendTileData = BlendTileData.Create(4, 4);
        var stack = new UndoRedoStack<IMapEdit>();

        stack.Push(BlendTileEdit.CreateSingle(blendTileData, 2, 2, 55));

        Assert.Equal(55, blendTileData.Tiles[2, 2]);

        stack.Undo();
        Assert.Equal(0, blendTileData.Tiles[2, 2]);

        stack.Redo();
        Assert.Equal(55, blendTileData.Tiles[2, 2]);
    }

    [Fact]
    public void HeightEditPushKeepsCapturedAfterState_AndCycles()
    {
        var heightMapData = HeightMapData.Create(0, new ushort[4, 4]);
        var stack = new UndoRedoStack<IMapEdit>();

        var edit = HeightMapEdit.Capture(heightMapData, 1, 1, 2, 2,
            elevations => elevations[1, 1] = 10, "brush");
        stack.Push(edit);

        Assert.Equal(10, heightMapData.Elevations[1, 1]);

        stack.Undo();
        Assert.Equal(0, heightMapData.Elevations[1, 1]);

        stack.Redo();
        Assert.Equal(10, heightMapData.Elevations[1, 1]);
    }
}
