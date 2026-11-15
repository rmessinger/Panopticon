using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

public sealed class RegionObjectEditTests
{
    private static MapObject NewObject(float x, float y = 0, float z = 0, string type = "TestObject")
    {
        return new MapObject(new Vector3(x, y, z), 0f, RoadType.None, type);
    }

    private static EditorMap CreateTestMap()
    {
        var mapFile = MapFile.CreateEmpty(64, 64, 6, 10, false);
        return EditorMap.Open(mapFile, "test_region_edit.map");
    }

    [Fact]
    public void QueryInRectangle_KnownSubsetOf1000Objects_ReturnsInExactMapOrder()
    {
        var objectsList = new ObjectsList();
        var allObjects = new MapObject[1000];
        for (var i = 0; i < allObjects.Length; i++)
        {
            allObjects[i] = NewObject(i * 2, i * 2, 0);
        }

        objectsList.AddObjects(allObjects);

        // Query rectangle covering x in [200, 300] and y in [200, 300] -> objects 100 to 150 inclusive
        var result = SelectTool.QueryInRectangle(objectsList, new Vector2(200, 200), new Vector2(300, 300));

        Assert.Equal(51, result.Count);
        for (var i = 0; i < result.Count; i++)
        {
            var expectedIndex = 100 + i;
            Assert.Same(allObjects[expectedIndex], result[i]);
            Assert.Equal(expectedIndex * 2, result[i].Position.X);
            Assert.Equal(expectedIndex * 2, result[i].Position.Y);
        }
    }

    [Fact]
    public void QueryInRectangle_ReversedDragEndpoints_NormalizesToSameSubset()
    {
        var objectsList = new ObjectsList();
        var allObjects = new MapObject[100];
        for (var i = 0; i < allObjects.Length; i++)
        {
            allObjects[i] = NewObject(i, i, 0);
        }

        objectsList.AddObjects(allObjects);

        var normal = SelectTool.QueryInRectangle(objectsList, new Vector2(10, 10), new Vector2(30, 30));
        var reversedX = SelectTool.QueryInRectangle(objectsList, new Vector2(30, 10), new Vector2(10, 30));
        var reversedY = SelectTool.QueryInRectangle(objectsList, new Vector2(10, 30), new Vector2(30, 10));
        var reversedBoth = SelectTool.QueryInRectangle(objectsList, new Vector2(30, 30), new Vector2(10, 10));

        Assert.Equal(21, normal.Count);
        Assert.Equal(normal.Count, reversedX.Count);
        Assert.Equal(normal.Count, reversedY.Count);
        Assert.Equal(normal.Count, reversedBoth.Count);

        for (var i = 0; i < normal.Count; i++)
        {
            Assert.Same(normal[i], reversedX[i]);
            Assert.Same(normal[i], reversedY[i]);
            Assert.Same(normal[i], reversedBoth[i]);
        }
    }

    [Fact]
    public void QueryInRectangle_ZeroWidthOrZeroHeight_ReturnsEmpty()
    {
        var objectsList = new ObjectsList();
        objectsList.AddObjects([NewObject(10, 10), NewObject(20, 20), NewObject(30, 30)]);

        // Zero width
        var zeroWidth = SelectTool.QueryInRectangle(objectsList, new Vector2(20, 10), new Vector2(20, 30));
        Assert.Empty(zeroWidth);

        // Zero height
        var zeroHeight = SelectTool.QueryInRectangle(objectsList, new Vector2(10, 20), new Vector2(30, 20));
        Assert.Empty(zeroHeight);

        // Point rectangle (zero width and height)
        var pointRect = SelectTool.QueryInRectangle(objectsList, new Vector2(20, 20), new Vector2(20, 20));
        Assert.Empty(pointRect);
    }

    [Fact]
    public void MoveSelection_SinglePush_UndoRestoresExactPositionsAndMapOrder_RedoReapplies()
    {
        var map = CreateTestMap();
        var objectsList = map.MapFile.ObjectsList!;
        var objects = new[]
        {
            NewObject(10, 10, 1),
            NewObject(20, 20, 2),
            NewObject(30, 30, 3),
        };
        objectsList.AddObjects(objects);

        var originalArray = objectsList.Objects;
        var edit = new RegionObjectMoveEdit(objects, new Vector3(5, -10, 20));
        map.UndoStack.Push(edit);

        // Assert applied positions
        Assert.Equal(new Vector3(15, 0, 21), objects[0].Position);
        Assert.Equal(new Vector3(25, 10, 22), objects[1].Position);
        Assert.Equal(new Vector3(35, 20, 23), objects[2].Position);

        // Map array unchanged (no per-object array copies)
        Assert.Same(originalArray, objectsList.Objects);

        // Exactly one undo entry
        Assert.True(map.UndoStack.CanUndo);
        Assert.False(map.UndoStack.CanRedo);

        map.UndoStack.Undo();
        Assert.False(map.UndoStack.CanUndo);
        Assert.True(map.UndoStack.CanRedo);

        // Assert restored exact positions and map order
        Assert.Equal(new Vector3(10, 10, 1), objects[0].Position);
        Assert.Equal(new Vector3(20, 20, 2), objects[1].Position);
        Assert.Equal(new Vector3(30, 30, 3), objects[2].Position);
        Assert.Same(originalArray, objectsList.Objects);

        map.UndoStack.Redo();
        Assert.False(map.UndoStack.CanRedo);
        Assert.True(map.UndoStack.CanUndo);

        // Assert reapplied positions
        Assert.Equal(new Vector3(15, 0, 21), objects[0].Position);
        Assert.Equal(new Vector3(25, 10, 22), objects[1].Position);
        Assert.Equal(new Vector3(35, 20, 23), objects[2].Position);
        Assert.Same(originalArray, objectsList.Objects);
    }

    [Fact]
    public void DeleteSelection_SinglePush_UndoRestoresInterleavingAndReferenceIdentity_RedoReDeletes()
    {
        var map = CreateTestMap();
        var objectsList = map.MapFile.ObjectsList!;
        var o0 = NewObject(0);
        var o1 = NewObject(1);
        var o2 = NewObject(2);
        var o3 = NewObject(3);
        var o4 = NewObject(4);
        var original = new[] { o0, o1, o2, o3, o4 };
        objectsList.AddObjects(original);

        // Interleaved deletion: o1 and o3
        var toDelete = new[] { o1, o3 };
        var edit = new RegionObjectDeleteEdit(objectsList, toDelete);
        map.UndoStack.Push(edit);

        Assert.Equal(3, objectsList.Objects.Length);
        Assert.Same(o0, objectsList.Objects[0]);
        Assert.Same(o2, objectsList.Objects[1]);
        Assert.Same(o4, objectsList.Objects[2]);

        // One undo restores full original sequence with reference identity
        Assert.True(map.UndoStack.CanUndo);
        map.UndoStack.Undo();
        Assert.False(map.UndoStack.CanUndo);

        Assert.Equal(5, objectsList.Objects.Length);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.Same(original[i], objectsList.Objects[i]);
        }

        // Redo re-deletes exactly
        map.UndoStack.Redo();
        Assert.Equal(3, objectsList.Objects.Length);
        Assert.Same(o0, objectsList.Objects[0]);
        Assert.Same(o2, objectsList.Objects[1]);
        Assert.Same(o4, objectsList.Objects[2]);
    }

    [Fact]
    public void EmptySelection_MoveAndDelete_PushNothingAndDoNotDirty()
    {
        var map = CreateTestMap();
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        var tool = new SelectTool();
        Assert.Empty(tool.SelectedObjects);

        // Move with empty selection
        tool.MoveSelectionBy(map, new Vector3(10, 0, 0));
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        // Delete with empty selection
        tool.DeleteSelection(map);
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        // Move with non-empty selection but delta == Vector3.Zero
        var obj = NewObject(10, 10);
        map.MapFile.ObjectsList!.AddObject(obj);

        // Simulate selection via drag
        tool.OnPointerDown(new ViewportPointerEvent
        {
            Position = new Vector2(0, 0),
            EventType = ViewportPointerEventType.Down,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(0, 0, 0),
        }, map);

        tool.OnPointerMove(new ViewportPointerEvent
        {
            Position = new Vector2(50, 50),
            EventType = ViewportPointerEventType.Move,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(50, 50, 0),
        }, map);

        tool.OnPointerUp(new ViewportPointerEvent
        {
            Position = new Vector2(50, 50),
            EventType = ViewportPointerEventType.Up,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(50, 50, 0),
        }, map);

        Assert.Single(tool.SelectedObjects);

        // Calling MoveSelectionBy with Vector3.Zero must no-op (no push, no dirty)
        var preDirty = map.IsDirty;
        var preCanUndo = map.UndoStack.CanUndo;
        tool.MoveSelectionBy(map, Vector3.Zero);
        Assert.Equal(preDirty, map.IsDirty);
        Assert.Equal(preCanUndo, map.UndoStack.CanUndo);
    }

    [Fact]
    public void SnapshotIdentity_MutatingPositionAfterEditCreation_DoesNotCorruptRevert_AndRestoreKeepsSameReferences()
    {
        var objectsList = new ObjectsList();
        var obj = NewObject(10, 20, 30);
        objectsList.AddObject(obj);

        var moveEdit = new RegionObjectMoveEdit([obj], new Vector3(5, 5, 5));

        // External mutation after edit creation
        obj.Position = new Vector3(999, 999, 999);

        // Reverting restores captured before-position value copy, not the mutated value
        moveEdit.Revert();
        Assert.Equal(new Vector3(10, 20, 30), obj.Position);

        // Delete restore keeps exact references
        var deleteEdit = new RegionObjectDeleteEdit(objectsList, [obj]);
        deleteEdit.Apply();
        Assert.Empty(objectsList.Objects);

        deleteEdit.Revert();
        var restored = Assert.Single(objectsList.Objects);
        Assert.Same(obj, restored);
    }

    [Fact]
    public void SingleClickSelection_ClosestWithinRadiusSelected_BeyondRadiusNone()
    {
        var map = CreateTestMap();
        var objectsList = map.MapFile.ObjectsList!;
        var near = NewObject(10, 10, 0);
        var far = NewObject(100, 100, 0);
        objectsList.AddObjects([near, far]);

        var tool = new SelectTool();
        MapObject? changedSelection = null;
        tool.SelectionChanged += obj => changedSelection = obj;

        // Click at (15, 10, 0): dist = 5 (< 20 selection radius)
        tool.OnPointerDown(new ViewportPointerEvent
        {
            Position = new Vector2(15, 10),
            EventType = ViewportPointerEventType.Down,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(15, 10, 0),
        }, map);

        Assert.Same(near, tool.SelectedObject);
        Assert.Same(near, changedSelection);

        // Click at (50, 50, 0): dist to near is ~56.5, dist to far is ~70.7 (> 20)
        tool.OnPointerDown(new ViewportPointerEvent
        {
            Position = new Vector2(50, 50),
            EventType = ViewportPointerEventType.Down,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(50, 50, 0),
        }, map);

        Assert.Null(tool.SelectedObject);
        Assert.Null(changedSelection);
    }

    [Fact]
    public void DragGesture_BeyondThresholdPopulatesSelectedObjects_SubThresholdDoesNot()
    {
        var map = CreateTestMap();
        var objectsList = map.MapFile.ObjectsList!;
        var obj1 = NewObject(30, 30, 0);
        var obj2 = NewObject(40, 40, 0);
        objectsList.AddObjects([obj1, obj2]);

        var tool = new SelectTool();

        // 1. Sub-threshold drag: down at (0,0,0), move to (10,10,0) (dist ~14.14 < 20), up at (10,10,0)
        tool.OnPointerDown(new ViewportPointerEvent
        {
            Position = new Vector2(0, 0),
            EventType = ViewportPointerEventType.Down,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(0, 0, 0),
        }, map);

        tool.OnPointerMove(new ViewportPointerEvent
        {
            Position = new Vector2(10, 10),
            EventType = ViewportPointerEventType.Move,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(10, 10, 0),
        }, map);

        Assert.False(tool.IsRegionDragActive);
        Assert.Null(tool.CurrentDragRectangle);

        tool.OnPointerUp(new ViewportPointerEvent
        {
            Position = new Vector2(10, 10),
            EventType = ViewportPointerEventType.Up,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(10, 10, 0),
        }, map);

        Assert.Empty(tool.SelectedObjects);

        // 2. Drag beyond threshold: down at (20,20,0), move to (50,50,0) (dist ~42.4 > 20)
        tool.OnPointerDown(new ViewportPointerEvent
        {
            Position = new Vector2(20, 20),
            EventType = ViewportPointerEventType.Down,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(20, 20, 0),
        }, map);

        tool.OnPointerMove(new ViewportPointerEvent
        {
            Position = new Vector2(50, 50),
            EventType = ViewportPointerEventType.Move,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(50, 50, 0),
        }, map);

        Assert.True(tool.IsRegionDragActive);
        Assert.NotNull(tool.CurrentDragRectangle);
        Assert.Equal(new Vector2(20, 20), tool.CurrentDragRectangle.Value.Min);
        Assert.Equal(new Vector2(50, 50), tool.CurrentDragRectangle.Value.Max);

        tool.OnPointerUp(new ViewportPointerEvent
        {
            Position = new Vector2(50, 50),
            EventType = ViewportPointerEventType.Up,
            Button = ViewportMouseButton.Left,
            WorldPosition = new Vector3(50, 50, 0),
        }, map);

        Assert.False(tool.IsRegionDragActive);
        Assert.Null(tool.CurrentDragRectangle);
        Assert.Equal(2, tool.SelectedObjects.Count);
        Assert.Same(obj1, tool.SelectedObjects[0]);
        Assert.Same(obj2, tool.SelectedObjects[1]);
    }

    [Fact]
    public void ObjectsList_ReplaceContents_ValidatesAndReplacesArrayReference()
    {
        var list = new ObjectsList();
        var initial = list.Objects;

        // Null argument throws ArgumentNullException
        Assert.Throws<ArgumentNullException>(() => list.ReplaceContents(null!));
        Assert.Same(initial, list.Objects);

        // Null element throws ArgumentException
        Assert.Throws<ArgumentException>(() => list.ReplaceContents([NewObject(1), null!]));
        Assert.Same(initial, list.Objects);

        // Duplicate reference throws ArgumentException
        var duplicate = NewObject(1);
        Assert.Throws<ArgumentException>(() => list.ReplaceContents([duplicate, duplicate]));
        Assert.Same(initial, list.Objects);

        // Valid array assigns reference directly
        var valid = new[] { NewObject(1), NewObject(2) };
        list.ReplaceContents(valid);
        Assert.Same(valid, list.Objects);
    }
}
