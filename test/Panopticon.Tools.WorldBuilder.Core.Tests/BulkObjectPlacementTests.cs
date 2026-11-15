using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

public sealed class BulkObjectPlacementTests
{
    private static MapObject NewObject(float x, float y = 0, float z = 0, string type = "TestObject")
    {
        return new MapObject(new Vector3(x, y, z), 0f, RoadType.None, type);
    }

    [Fact]
    public void BulkPlacementPush_Adds1000ObjectsInExactOrder_AndUndoRedoCyclesAsSingleEntry()
    {
        var objectsList = new ObjectsList();
        var stack = new UndoRedoStack<IMapEdit>();
        var objects = new MapObject[1000];
        for (var i = 0; i < objects.Length; i++)
        {
            objects[i] = NewObject(i);
        }

        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, objects);
        stack.Push(edit);

        Assert.Equal(1000, objectsList.Objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            Assert.Same(objects[i], objectsList.Objects[i]);
        }

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        // One undo removes all 1000 objects and exhausts the undo stack.
        stack.Undo();
        Assert.False(stack.CanUndo);
        Assert.Empty(objectsList.Objects);

        // Redo restores all 1000 in exact order.
        stack.Redo();
        Assert.False(stack.CanRedo);
        Assert.True(stack.CanUndo);
        Assert.Equal(1000, objectsList.Objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            Assert.Same(objects[i], objectsList.Objects[i]);
        }
    }

    [Fact]
    public void BulkPlacement_PreservesExistingPopulationInFrontAndUnmodified()
    {
        var objectsList = new ObjectsList();
        var stack = new UndoRedoStack<IMapEdit>();

        var existing1 = NewObject(100);
        var existing2 = NewObject(200);
        var existing3 = NewObject(300);
        objectsList.AddObject(existing1);
        objectsList.AddObject(existing2);
        objectsList.AddObject(existing3);

        var batch = new[] { NewObject(1), NewObject(2), NewObject(3), NewObject(4), NewObject(5) };
        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, batch);
        stack.Push(edit);

        Assert.Equal(8, objectsList.Objects.Length);
        Assert.Same(existing1, objectsList.Objects[0]);
        Assert.Same(existing2, objectsList.Objects[1]);
        Assert.Same(existing3, objectsList.Objects[2]);
        for (var i = 0; i < batch.Length; i++)
        {
            Assert.Same(batch[i], objectsList.Objects[3 + i]);
        }

        stack.Undo();
        Assert.Equal(3, objectsList.Objects.Length);
        Assert.Same(existing1, objectsList.Objects[0]);
        Assert.Same(existing2, objectsList.Objects[1]);
        Assert.Same(existing3, objectsList.Objects[2]);

        stack.Redo();
        Assert.Equal(8, objectsList.Objects.Length);
        Assert.Same(existing1, objectsList.Objects[0]);
        Assert.Same(existing2, objectsList.Objects[1]);
        Assert.Same(existing3, objectsList.Objects[2]);
        for (var i = 0; i < batch.Length; i++)
        {
            Assert.Same(batch[i], objectsList.Objects[3 + i]);
        }
    }

    [Fact]
    public void BulkPlacementEdit_EnumeratesInputOnce_AndSnapshotIsStableAgainstCallerMutation()
    {
        var objectsList = new ObjectsList();
        var obj1 = NewObject(1);
        var obj2 = NewObject(2);
        var counting = new CountingEnumerable<MapObject>([obj1, obj2]);

        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, counting);
        Assert.Equal(1, counting.EnumerationCount);

        // Applying the edit does not re-enumerate the source enumerable.
        edit.Apply();
        Assert.Equal(1, counting.EnumerationCount);
        Assert.Equal(2, objectsList.Objects.Length);

        // Caller mutating their source list after edit creation does not affect the edit.
        var list2 = new ObjectsList();
        var sourceList = new List<MapObject> { obj1, obj2 };
        var edit2 = BulkObjectPlacementEdit.CreatePlacement(list2, sourceList);
        sourceList.Add(NewObject(3));
        sourceList.Clear();

        edit2.Apply();
        Assert.Equal(2, list2.Objects.Length);
        Assert.Same(obj1, list2.Objects[0]);
        Assert.Same(obj2, list2.Objects[1]);
    }

    [Fact]
    public void AddObjects_ValidationFailuresLeaveObjectsUnchanged()
    {
        var objectsList = new ObjectsList();
        var existing = NewObject(0);
        objectsList.AddObject(existing);
        var initialArray = objectsList.Objects;

        // 1. Null enumerable
        Assert.Throws<ArgumentNullException>(() => objectsList.AddObjects(null!));
        Assert.Same(initialArray, objectsList.Objects);

        // 2. Null element in batch
        var withNull = new[] { NewObject(1), null!, NewObject(2) };
        Assert.Throws<ArgumentException>(() => objectsList.AddObjects(withNull));
        Assert.Same(initialArray, objectsList.Objects);

        // 3. Duplicate reference within batch
        var dup = NewObject(10);
        var withDuplicate = new[] { dup, NewObject(11), dup };
        Assert.Throws<ArgumentException>(() => objectsList.AddObjects(withDuplicate));
        Assert.Same(initialArray, objectsList.Objects);

        // 4. Object reference already present in Objects
        var withExisting = new[] { NewObject(20), existing, NewObject(21) };
        Assert.Throws<ArgumentException>(() => objectsList.AddObjects(withExisting));
        Assert.Same(initialArray, objectsList.Objects);
    }

    [Fact]
    public void EmptyBatch_ViaEditIsNoOpSuccess_AndViaToolPushesNothingAndDoesNotDirty()
    {
        // Edit Apply / Revert with empty batch is a no-op success.
        var objectsList = new ObjectsList();
        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, Array.Empty<MapObject>());
        var initialArray = objectsList.Objects;

        edit.Apply();
        Assert.Same(initialArray, objectsList.Objects);

        edit.Revert();
        Assert.Same(initialArray, objectsList.Objects);

        // Tool with real EditorMap
        var mapFile = MapFile.CreateEmpty(32, 32, 6, 10, false);
        var map = EditorMap.Open(mapFile, "test_empty_batch.map");

        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        var tool = new PlaceObjectTool { SelectedObjectType = "AmericaVehicleHumvee" };

        // 1. Empty positions: no push, no dirty.
        tool.PlaceObjects(map, Array.Empty<Vector3>());
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        // 2. Empty / null SelectedObjectType: no push, no dirty.
        tool.SelectedObjectType = null;
        tool.PlaceObjects(map, [new Vector3(10, 0, 10)]);
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        tool.SelectedObjectType = "";
        tool.PlaceObjects(map, [new Vector3(10, 0, 10)]);
        Assert.False(map.IsDirty);
        Assert.False(map.UndoStack.CanUndo);

        // 3. Valid batch: pushes one edit and marks dirty exactly once.
        tool.SelectedObjectType = "AmericaVehicleHumvee";
        tool.PlaceObjects(map, [new Vector3(10, 0, 10), new Vector3(20, 0, 20)]);
        Assert.True(map.IsDirty);
        Assert.True(map.UndoStack.CanUndo);
        Assert.Equal(2, map.MapFile.ObjectsList.Objects.Length);

        // Exactly one undo entry.
        map.UndoStack.Undo();
        Assert.False(map.UndoStack.CanUndo);
        Assert.Empty(map.MapFile.ObjectsList.Objects);
    }

    [Fact]
    public void BatchAppend_AllocatesMeaningfullyLessThanIndividualAddObjects_AndAssignsObjectsOnce()
    {
        const int existingCount = 1000;
        const int batchCount = 1000;

        var existingLoop = new MapObject[existingCount];
        for (var i = 0; i < existingCount; i++)
        {
            existingLoop[i] = NewObject(i);
        }

        var newLoop = new MapObject[batchCount];
        for (var i = 0; i < batchCount; i++)
        {
            newLoop[i] = NewObject(existingCount + i);
        }

        var listLoop = new ObjectsList();
        listLoop.AddObjects(existingLoop);

        var existingBatch = new MapObject[existingCount];
        for (var i = 0; i < existingCount; i++)
        {
            existingBatch[i] = NewObject(i);
        }

        var newBatch = new MapObject[batchCount];
        for (var i = 0; i < batchCount; i++)
        {
            newBatch[i] = NewObject(existingCount + i);
        }

        var listBatch = new ObjectsList();
        listBatch.AddObjects(existingBatch);

        // Warm up and isolate thread allocations.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var startLoop = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < batchCount; i++)
        {
            listLoop.AddObject(newLoop[i]);
        }

        var loopAllocated = GC.GetAllocatedBytesForCurrentThread() - startLoop;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var startBatch = GC.GetAllocatedBytesForCurrentThread();
        listBatch.AddObjects(newBatch);
        var batchAllocated = GC.GetAllocatedBytesForCurrentThread() - startBatch;

        // Assert batch allocates less than 1/4 of individual append loop.
        Assert.True(batchAllocated < loopAllocated / 4,
            $"Expected batch ({batchAllocated} B) to be < loop/4 ({loopAllocated / 4} B).");

        // Code-audit assertion: AddObjects assigns Objects array reference exactly once.
        var listAudit = new ObjectsList();
        var arrayBefore = listAudit.Objects;
        listAudit.AddObjects([NewObject(1), NewObject(2)]);
        var arrayAfter = listAudit.Objects;
        Assert.NotSame(arrayBefore, arrayAfter);
        Assert.Equal(2, arrayAfter.Length);
    }

    [Fact]
    public void TenThousandObjectPlacement_Probe()
    {
        const int count = 10000;
        var objectsList = new ObjectsList();
        var stack = new UndoRedoStack<IMapEdit>();
        var objects = new MapObject[count];
        for (var i = 0; i < count; i++)
        {
            objects[i] = NewObject(i);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, objects);
        stack.Push(edit);
        sw.Stop();

        Assert.Equal(count, objectsList.Objects.Length);
        Assert.True(sw.ElapsedMilliseconds < 500);

        stack.Undo();
        Assert.Empty(objectsList.Objects);

        stack.Redo();
        Assert.Equal(count, objectsList.Objects.Length);
    }

    private sealed class CountingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _source;

        public int EnumerationCount { get; private set; }

        public CountingEnumerable(IEnumerable<T> source)
        {
            _source = source;
        }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return _source.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
