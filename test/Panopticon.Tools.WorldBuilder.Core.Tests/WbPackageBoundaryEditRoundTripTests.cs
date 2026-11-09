#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-H6-02 edit round-trip tests: one real height brush-style edit and one real placed-object
/// edit applied through the editor's edit surface (HeightBrushTool / PlaceObjectTool pointer
/// path), saved through the package boundary, reopened, and compared against the edited
/// in-memory state for heights, objects, and companions.
/// </summary>
public sealed class WbPackageBoundaryEditRoundTripTests : IDisposable
{
    private const float PositionTolerance = 1e-4f;

    private readonly string _tempRoot;
    private readonly ITestOutputHelper _output;

    public WbPackageBoundaryEditRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "wb-pkg-edit-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HeightBrushMoundStroke_SaveReopen_HeightSamplesEqualEditedState()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var before = (ushort[,])heightMapData.Elevations.Clone();
        var centerWorld = CenterWorld(heightMapData);
        var centerCell = WorldToCell(centerWorld, heightMapData.BorderWidth);

        var brush = new HeightBrushTool
        {
            Mode = HeightBrushMode.Mound,
            Brush = { RadiusCells = 3, Strength = 1f, HeightDelta = 100 },
        };
        brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, centerWorld), editorMap);
        brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, centerWorld), editorMap);

        Assert.Equal(before[centerCell.x, centerCell.y] + 100, heightMapData.Elevations[centerCell.x, centerCell.y]);

        SnapshotState(editorMap, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        editorMap.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        AssertRoundTripEquality(reopened, editedElevations, editedObjects, editedIni, editedStr);
    }

    [Fact]
    public void HeightBrushDigStroke_SaveReopen_HeightSamplesEqualEditedState()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var before = (ushort[,])heightMapData.Elevations.Clone();
        var centerWorld = CenterWorld(heightMapData);
        var centerCell = WorldToCell(centerWorld, heightMapData.BorderWidth);

        var brush = new HeightBrushTool
        {
            Mode = HeightBrushMode.Dig,
            Brush = { RadiusCells = 3, Strength = 1f, HeightDelta = 5 },
        };
        brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, centerWorld), editorMap);
        brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, centerWorld), editorMap);

        Assert.Equal(before[centerCell.x, centerCell.y] - 5, heightMapData.Elevations[centerCell.x, centerCell.y]);

        SnapshotState(editorMap, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        editorMap.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        AssertRoundTripEquality(reopened, editedElevations, editedObjects, editedIni, editedStr);
    }

    [Fact]
    public void PlacedObjectPlacement_SaveReopen_ObjectEqualsEditedState()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var beforeCount = editorMap.MapFile.ObjectsList.Objects.Length;
        var placeWorld = OffsetWorld(editorMap.MapFile.HeightMapData!, 60, 40);

        var placer = new PlaceObjectTool { SelectedObjectType = "AmericaVehicleHumvee" };
        placer.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, placeWorld), editorMap);

        Assert.Equal(beforeCount + 1, editorMap.MapFile.ObjectsList.Objects.Length);

        SnapshotState(editorMap, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        editorMap.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        AssertRoundTripEquality(reopened, editedElevations, editedObjects, editedIni, editedStr);

        var placed = Assert.Single(
            reopened.MapFile.ObjectsList.Objects,
            o => o.TypeName == "AmericaVehicleHumvee");
        AssertPositionEqual(placeWorld, placed.Position);
    }

    [Fact]
    public void PlacedObjectPropertyModification_SaveReopen_PropertyEqualsEditedState()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var target = editorMap.MapFile.ObjectsList.Objects[0];
        target.Properties.AddReal("H6EditedProp", 42.5f);

        SnapshotState(editorMap, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        editorMap.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        AssertRoundTripEquality(reopened, editedElevations, editedObjects, editedIni, editedStr);

        var reloaded = reopened.MapFile.ObjectsList.Objects[0];
        var prop = reloaded.Properties.GetPropOrNull("H6EditedProp");
        Assert.NotNull(prop);
        Assert.Equal(42.5f, prop!.Value);
    }

    [Fact]
    public void BrushStrokeAndPlacementWithCompanionEdits_SaveReopen_FullStateEquality()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var heightMapData = editorMap.MapFile.HeightMapData!;
        var beforeElevations = (ushort[,])heightMapData.Elevations.Clone();
        var beforeObjectCount = editorMap.MapFile.ObjectsList.Objects.Length;
        var centerWorld = CenterWorld(heightMapData);

        var brush = new HeightBrushTool
        {
            Mode = HeightBrushMode.Mound,
            Brush = { RadiusCells = 5, Strength = 1f, HeightDelta = 100 },
        };
        brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, centerWorld), editorMap);
        brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, centerWorld), editorMap);

        var placer = new PlaceObjectTool { SelectedObjectType = "AmericaInfantryRanger" };
        placer.OnPointerDown(
            PointerEvent(ViewportPointerEventType.Down, OffsetWorld(heightMapData, 40, -30)),
            editorMap);

        editorMap.MapIniContent = "; h6-02 edited companion ini\r\nEditedFlag = Yes\r\n";
        editorMap.MapStrContent = "MAPEDIT:H6Key=\"H6 Edited Value\"\r\n";
        editorMap.MarkDirty();

        Assert.False(
            beforeElevations.Cast<ushort>().SequenceEqual(heightMapData.Elevations.Cast<ushort>()),
            "The brush stroke must change at least one elevation sample.");
        Assert.Equal(beforeObjectCount + 1, editorMap.MapFile.ObjectsList.Objects.Length);

        SnapshotState(editorMap, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        editorMap.Save();

        Assert.False(editorMap.IsDirty);

        var reopened = EditorMap.OpenPackage(pkgDir);
        Assert.True(reopened.IsPackage);
        AssertRoundTripEquality(reopened, editedElevations, editedObjects, editedIni, editedStr);

        var iniDisk = File.ReadAllText(Path.Combine(pkgDir, "companions", "map.ini"));
        Assert.Equal(editedIni, iniDisk);
    }

    [Fact]
    public void CompanionEditsOnly_SaveReopen_CompanionsEqualAndHeightsUntouched()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var heightSnapshot = (ushort[,])editorMap.MapFile.HeightMapData!.Elevations.Clone();

        editorMap.MapIniContent = "; companion-only edit\r\n";
        editorMap.MarkDirty();
        editorMap.Save();

        var reopened = EditorMap.OpenPackage(pkgDir);
        Assert.Equal("; companion-only edit\r\n", reopened.MapIniContent);
        AssertElevationsEqual(heightSnapshot, reopened.MapFile.HeightMapData!.Elevations);
    }

    [Fact]
    public void ReopenedEditorMap_SecondEditRoundTrip_Persists()
    {
        var editorMap = OpenSyntheticPackage(out var pkgDir);
        var heightMapData = editorMap.MapFile.HeightMapData!;

        var brush = new HeightBrushTool
        {
            Mode = HeightBrushMode.Mound,
            Brush = { RadiusCells = 3, Strength = 1f, HeightDelta = 50 },
        };
        brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, CenterWorld(heightMapData)), editorMap);
        brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, CenterWorld(heightMapData)), editorMap);
        editorMap.Save();

        // Second session: reopen, edit again, save, reopen.
        var second = EditorMap.OpenPackage(pkgDir);
        var secondHeightMap = second.MapFile.HeightMapData!;
        brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, CenterWorld(secondHeightMap)), second);
        brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, CenterWorld(secondHeightMap)), second);

        SnapshotState(second, out var editedElevations, out var editedObjects, out var editedIni, out var editedStr);
        second.Save();

        var third = EditorMap.OpenPackage(pkgDir);
        AssertRoundTripEquality(third, editedElevations, editedObjects, editedIni, editedStr);
    }

    // ── Fixture and assertion helpers ──────────────────────────────────────

    private EditorMap OpenSyntheticPackage(out string pkgDir)
    {
        var mapFile = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
            BorderWidth = 6,
        }).MapFile;

        var seeded = new MapObject(new Vector3(100, 100, 0), 0f, RoadType.None, "CustomUnitTemplate");
        seeded.Properties.AddReal("InitialProp", 1.0f);
        mapFile.ObjectsList.AddObject(seeded);

        pkgDir = Path.Combine(_tempRoot, "edit-rt-package");
        var writeResult = MapPackageConverter.SavePackage(
            mapFile,
            pkgDir,
            "; initial map.ini\n",
            "MAPEDIT:Title=\"Round Trip\"\n");
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics.Select(d => d.Message)));

        return EditorMap.OpenPackage(pkgDir);
    }

    private static void SnapshotState(
        EditorMap editorMap,
        out ushort[,] elevations,
        out (string TypeName, Vector3 Position, float Angle, RoadType RoadType)[] objects,
        out string mapIni,
        out string mapStr)
    {
        elevations = (ushort[,])editorMap.MapFile.HeightMapData!.Elevations.Clone();
        objects = editorMap.MapFile.ObjectsList.Objects
            .Select(o => (o.TypeName, o.Position, o.Angle, o.RoadType))
            .ToArray();
        mapIni = editorMap.MapIniContent;
        mapStr = editorMap.MapStrContent;
    }

    private static void AssertRoundTripEquality(
        EditorMap reopened,
        ushort[,] editedElevations,
        (string TypeName, Vector3 Position, float Angle, RoadType RoadType)[] editedObjects,
        string editedIni,
        string editedStr)
    {
        Assert.False(reopened.IsDirty);
        AssertElevationsEqual(editedElevations, reopened.MapFile.HeightMapData!.Elevations);
        AssertObjectsEqual(editedObjects, reopened.MapFile.ObjectsList.Objects);
        Assert.Equal(editedIni, reopened.MapIniContent);
        Assert.Equal(editedStr, reopened.MapStrContent);
    }

    private static void AssertElevationsEqual(ushort[,] expected, ushort[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.True(
            expected.Cast<ushort>().SequenceEqual(actual.Cast<ushort>()),
            "Reopened elevation samples differ from the edited in-memory state.");
    }

    private static void AssertObjectsEqual(
        (string TypeName, Vector3 Position, float Angle, RoadType RoadType)[] expected,
        MapObject[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].TypeName, actual[index].TypeName);
            AssertPositionEqual(expected[index].Position, actual[index].Position);
            Assert.Equal(expected[index].Angle, actual[index].Angle);
            Assert.Equal(expected[index].RoadType, actual[index].RoadType);
        }
    }

    private static void AssertPositionEqual(Vector3 expected, Vector3 actual)
    {
        Assert.True(
            Vector3.Distance(expected, actual) <= PositionTolerance,
            $"Position {actual} differs from expected {expected} by more than {PositionTolerance} m.");
    }

    private static Vector3 CenterWorld(HeightMapData heightMapData) => OffsetWorld(heightMapData, 0, 0);

    private static Vector3 OffsetWorld(HeightMapData heightMapData, int offsetX, int offsetY)
    {
        var cell = WorldOffsetToCell(heightMapData, offsetX, offsetY);
        return new Vector3((cell.x - (int)heightMapData.BorderWidth) * HeightMap.HorizontalScale + 5f, (cell.y - (int)heightMapData.BorderWidth) * HeightMap.HorizontalScale + 5f, 0f);
    }

    private static (int x, int y) WorldOffsetToCell(HeightMapData heightMapData, int offsetX, int offsetY)
    {
        return (
            heightMapData.Elevations.GetLength(0) / 2 + offsetX,
            heightMapData.Elevations.GetLength(1) / 2 + offsetY);
    }

    private static (int x, int y) WorldToCell(Vector3 worldPosition, uint borderWidth)
    {
        return (
            (int)(worldPosition.X / HeightMap.HorizontalScale) + (int)borderWidth,
            (int)(worldPosition.Y / HeightMap.HorizontalScale) + (int)borderWidth);
    }

    private static ViewportPointerEvent PointerEvent(ViewportPointerEventType eventType, Vector3 worldPosition) => new()
    {
        Position = new Vector2(worldPosition.X, worldPosition.Y),
        EventType = eventType,
        Button = ViewportMouseButton.Left,
        WorldPosition = worldPosition,
    };
}
