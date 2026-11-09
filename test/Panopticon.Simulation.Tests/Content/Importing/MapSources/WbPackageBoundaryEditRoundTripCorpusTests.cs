#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Tests.Data;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources;

/// <summary>
/// WI-H6-02 installed-corpus edit round-trip test: converts a real map from the game
/// installation into an H3 package, opens it through the editor boundary, applies one real
/// height brush stroke and one real placed-object edit plus companion edits through the
/// editor's public edit surface, saves through the package save path, reopens, and asserts
/// the reopened state equals the edited in-memory state. Vacuous skip without an install.
/// </summary>
public sealed class WbPackageBoundaryEditRoundTripCorpusTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ITestOutputHelper _output;

    public WbPackageBoundaryEditRoundTripCorpusTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "wb-pkg-edit-rt-corpus-" + Guid.NewGuid().ToString("N"));
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
    public void InstalledCorpus_ConvertOpenEditSaveReopen_StateEqualsEditedInMemoryState()
    {
        var corpusTested = false;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            if (corpusTested)
            {
                return;
            }

            MapFile mapFile;

            try
            {
                using var stream = entry.Open();
                using var decompressed = MapFile.Decompress(stream);
                mapFile = MapFile.FromStream(decompressed);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Skipping unparseable map {entry.FilePath}: {ex.Message}");
                return;
            }

            if (mapFile.HeightMapData is null || mapFile.ObjectsList is null)
            {
                _output.WriteLine($"Skipping map without height or object data: {entry.FilePath}");
                return;
            }

            if (mapFile.SidesList is { } sides && !LegacyMapReader.IsSupportedSidesListVersion(sides.Version))
            {
                _output.WriteLine($"Skipping map with unsupported sides version {sides.Version}: {entry.FilePath}");
                return;
            }

            var pkgDir = Path.Combine(_tempRoot, "corpus-edit-rt-pkg");
            const string initialIni = "; corpus edit round-trip map.ini\n";
            const string initialStr = "MAPEDIT:Corpus=\"EditRoundTrip\"\n";

            var writeResult = MapPackageConverter.SavePackage(mapFile, pkgDir, initialIni, initialStr);
            Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

            var editorMap = EditorMap.OpenPackage(pkgDir);
            Assert.True(editorMap.IsPackage);

            var heightMapData = editorMap.MapFile.HeightMapData!;
            var beforeElevations = (ushort[,])heightMapData.Elevations.Clone();
            var beforeObjectCount = editorMap.MapFile.ObjectsList.Objects.Length;

            // ONE real height brush-style edit through the editor's edit surface.
            var brush = new HeightBrushTool
            {
                Mode = HeightBrushMode.Mound,
                Brush = { RadiusCells = 5, Strength = 1f, HeightDelta = 100 },
            };
            brush.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, CenterWorld(heightMapData)), editorMap);
            brush.OnPointerUp(PointerEvent(ViewportPointerEventType.Up, CenterWorld(heightMapData)), editorMap);

            // ONE real placed-object edit through the same surface.
            var placer = new PlaceObjectTool { SelectedObjectType = "AmericaInfantryRanger" };
            var placeWorld = OffsetWorld(heightMapData, 40, -30);
            placer.OnPointerDown(PointerEvent(ViewportPointerEventType.Down, placeWorld), editorMap);

            // Companion edits through the editor's companion state.
            editorMap.MapIniContent = "; corpus edited companion ini\r\n";
            editorMap.MapStrContent = "MAPEDIT:CorpusEdited=\"Yes\"\r\n";
            editorMap.MarkDirty();

            Assert.False(
                beforeElevations.Cast<ushort>().SequenceEqual(heightMapData.Elevations.Cast<ushort>()),
                "The brush stroke must change at least one elevation sample.");
            Assert.Equal(beforeObjectCount + 1, editorMap.MapFile.ObjectsList.Objects.Length);

            var editedElevations = (ushort[,])heightMapData.Elevations.Clone();
            var editedObjects = editorMap.MapFile.ObjectsList.Objects
                .Select(o => (o.TypeName, o.Position, o.Angle, o.RoadType))
                .ToArray();
            const string editedIni = "; corpus edited companion ini\r\n";
            const string editedStr = "MAPEDIT:CorpusEdited=\"Yes\"\r\n";

            editorMap.Save();
            Assert.False(editorMap.IsDirty);

            var reopened = EditorMap.OpenPackage(pkgDir);
            Assert.False(reopened.IsDirty);

            AssertElevationsEqual(editedElevations, reopened.MapFile.HeightMapData!.Elevations);
            AssertObjectsEqual(editedObjects, reopened.MapFile.ObjectsList.Objects);
            Assert.Equal(editedIni, reopened.MapIniContent);
            Assert.Equal(editedStr, reopened.MapStrContent);

            var placed = Assert.Single(
                reopened.MapFile.ObjectsList.Objects,
                o => o.TypeName == "AmericaInfantryRanger");
            Assert.Equal(placeWorld, placed.Position);

            corpusTested = true;
            _output.WriteLine($"Corpus edit round-trip test passed for {entry.FilePath}.");
        });

        if (!corpusTested)
        {
            _output.WriteLine("No installed corpus map found; test passed vacuously.");
        }
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
            Assert.Equal(expected[index].Position, actual[index].Position);
            Assert.Equal(expected[index].Angle, actual[index].Angle);
            Assert.Equal(expected[index].RoadType, actual[index].RoadType);
        }
    }

    private static Vector3 CenterWorld(HeightMapData heightMapData) => OffsetWorld(heightMapData, 0, 0);

    private static Vector3 OffsetWorld(HeightMapData heightMapData, int offsetX, int offsetY)
    {
        var cellX = heightMapData.Elevations.GetLength(0) / 2 + offsetX;
        var cellY = heightMapData.Elevations.GetLength(1) / 2 + offsetY;
        var border = (int)heightMapData.BorderWidth;
        return new Vector3((cellX - border) * HeightMap.HorizontalScale + 5f, (cellY - border) * HeightMap.HorizontalScale + 5f, 0f);
    }

    private static ViewportPointerEvent PointerEvent(ViewportPointerEventType eventType, Vector3 worldPosition) => new()
    {
        Position = new Vector2(worldPosition.X, worldPosition.Y),
        EventType = eventType,
        Button = ViewportMouseButton.Left,
        WorldPosition = worldPosition,
    };
}
