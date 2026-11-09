#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Navigation;
using Panopticon.Network;
using Panopticon.Terrain;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources;

// The enclosing Panopticon namespace makes bare `Navigation` resolve to the namespace, not the class.
using RuntimeNavigation = Panopticon.Navigation.Navigation;
using ScriptingWaypoint = Panopticon.Scripting.Waypoint;
using ScriptingWaypoints = Panopticon.Scripting.WaypointCollection;

/// <summary>
/// H2 runtime bridge parity: for installed maps, LegacyMapReader.Read -> RuntimeBridge reproduces
/// the parsed HeightMapData and ObjectsList exactly, and materialized heights drive identical
/// navigation input. Synthetic cases cover the mask feed, the reader-source seam (Game.MapReaderFactory),
/// the representability guards, and no-partial-publication; the full Game.LoadMap gate boots the real
/// game and is Interactive. Corpus cases pass vacuously without an installation, mirroring LegacyMapReaderTests.
/// </summary>
[Collection("SerialGameTests")]
public sealed class RuntimeBridgeParityTests
{
    private const int CorpusValidatingTarget = 3;

    // A single full chunk: 128 intervals plus the shared seam point, so one chunk carries the lattice.
    private const int MaskedLatticeSize = 129;

    private const int StoredWidth = MapChunkGeometry.StoredPointsPerAxis;

    private const float LegacyVerticalScale = 0.625f;

    private const int InteriorStoredRow = 10;

    private const int InteriorStoredColumn = 10;

    private readonly ITestOutputHelper _output;

    public RuntimeBridgeParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InstalledMapsMaterializeExactlyFromParsedModels()
    {
        var filesSeen = 0;
        var validated = 0;
        var rejected = 0;
        var unparseable = 0;
        var unreadable = 0;
        var alpineAssaultAttempted = false;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            filesSeen++;

            var isAlpineAssault = entry.FilePath.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase);
            alpineAssaultAttempted |= isAlpineAssault;

            // The cap only bounds the corpus pass; Alpine Assault is attempted regardless so the
            // duplicate-waypoint map always proves parity.
            if (!isAlpineAssault && validated >= CorpusValidatingTarget)
            {
                return;
            }

            using var entryStream = OpenOrNull(entry);

            if (entryStream is null)
            {
                unreadable++;
                _output.WriteLine($"{entry.FilePath}: unreadable (dangling entry); skipped.");
                return;
            }

            MapFile mapFile;

            try
            {
                using var mapStream = MapFile.Decompress(entryStream);
                mapFile = MapFile.FromStream(mapStream);
            }
            catch (Exception exception)
            {
                unparseable++;
                _output.WriteLine($"{entry.FilePath}: unparseable ({exception.GetType().Name}); skipped.");
                return;
            }

            try
            {
                var model = new LegacyMapReader(mapFile, entry.FilePath).Read();
                var materializedHeights = RuntimeBridge.MaterializeHeightMapData(model);
                var materializedObjects = RuntimeBridge.MaterializeMapObjects(model);

                AssertHeightsMatchParsed(mapFile, materializedHeights);
                AssertObjectsMatchParsed(mapFile, materializedObjects);
                AssertWaypointSetMatchesStarredObjects(model, mapFile);

                validated++;
                _output.WriteLine(
                    $"{entry.FilePath}: parity ok, lattice {materializedHeights.Width}x{materializedHeights.Height}, " +
                    $"objects {materializedObjects.Length}, waypoints {model.Waypoints?.Waypoints.Count ?? 0}.");
            }
            catch (MapImportException exception)
            {
                rejected++;
                _output.WriteLine($"{entry.FilePath}: rejected ({string.Join("; ", exception.Diagnostics.Select(d => d.Code))}).");
            }
        });

        if (filesSeen == 0)
        {
            _output.WriteLine("No installed .map corpus found; skipped.");
            return;
        }

        _output.WriteLine(
            $"Corpus parity: {validated} validating, {rejected} rejected, {unparseable} unparseable, " +
            $"{unreadable} unreadable, Alpine Assault attempted {alpineAssaultAttempted}.");

        Assert.True(validated >= CorpusValidatingTarget, $"Expected at least {CorpusValidatingTarget} validating maps; got {validated}.");
    }

    [Fact]
    public void InstalledMapMaterializedHeightsDriveIdenticalNavigation()
    {
        var filesSeen = 0;
        var compared = 0;
        var rejected = 0;
        var unparseable = 0;
        var unreadable = 0;
        var alpineAssaultAttempted = false;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            filesSeen++;

            var isAlpineAssault = entry.FilePath.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase);
            alpineAssaultAttempted |= isAlpineAssault;

            if (!isAlpineAssault && compared >= CorpusValidatingTarget)
            {
                return;
            }

            using var entryStream = OpenOrNull(entry);

            if (entryStream is null)
            {
                unreadable++;
                _output.WriteLine($"{entry.FilePath}: unreadable (dangling entry); skipped.");
                return;
            }

            MapFile mapFile;

            try
            {
                using var mapStream = MapFile.Decompress(entryStream);
                mapFile = MapFile.FromStream(mapStream);
            }
            catch (Exception exception)
            {
                unparseable++;
                _output.WriteLine($"{entry.FilePath}: unparseable ({exception.GetType().Name}); skipped.");
                return;
            }

            try
            {
                var model = new LegacyMapReader(mapFile, entry.FilePath).Read();

                // Legacy models are heights-only: no mask feed, so navigation falls back to BlendTileData.
                var hasFeed = RuntimeBridge.TryGetMaskFeed(model, out var feed);
                Assert.False(hasFeed);
                Assert.Null(feed);

                var materialized = RuntimeBridge.MaterializeHeightMapData(model);
                var fromParsed = new RuntimeNavigation(mapFile.BlendTileData!, new HeightMap(mapFile.HeightMapData));
                var fromMaterialized = new RuntimeNavigation(mapFile.BlendTileData!, new HeightMap(materialized));

                Assert.Equal(fromParsed.Graph.Width, fromMaterialized.Graph.Width);
                Assert.Equal(fromParsed.Graph.Height, fromMaterialized.Graph.Height);

                for (var x = 0; x < fromParsed.Graph.Width; x++)
                {
                    for (var y = 0; y < fromParsed.Graph.Height; y++)
                    {
                        var expected = fromParsed.Graph.GetNode(x, y).Passability;
                        var actual = fromMaterialized.Graph.GetNode(x, y).Passability;

                        Assert.True(expected == actual, $"Navigation node ({x}, {y}): parsed {expected} != materialized {actual}.");
                    }
                }

                compared++;
                _output.WriteLine($"{entry.FilePath}: navigation parity ok, graph {fromParsed.Graph.Width}x{fromParsed.Graph.Height}.");
            }
            catch (MapImportException exception)
            {
                rejected++;
                _output.WriteLine($"{entry.FilePath}: rejected ({string.Join("; ", exception.Diagnostics.Select(d => d.Code))}).");
            }
        });

        if (filesSeen == 0)
        {
            _output.WriteLine("No installed .map corpus found; skipped.");
            return;
        }

        _output.WriteLine(
            $"Corpus navigation parity: {compared} compared, {rejected} rejected, {unparseable} unparseable, " +
            $"{unreadable} unreadable, Alpine Assault attempted {alpineAssaultAttempted}.");

        Assert.True(compared >= CorpusValidatingTarget, $"Expected at least {CorpusValidatingTarget} compared maps; got {compared}.");
    }

    [Fact]
    public void MaskedReaderFeedsImpassabilityAndNavigationThroughFactorySeam()
    {
        var original = Game.MapReaderFactory;
        var reader = new FixedModelReader(BuildSingleChunkModel(masks: true));

        Game.MapReaderFactory = (_, _) => reader;

        try
        {
            // Driven exactly as Game.LoadMap drives it.
            var model = Game.MapReaderFactory(CreateStubEntry(), CreateEmptyMap()).Read();
            Assert.Same(reader.Model, model);

            var hasFeed = RuntimeBridge.TryGetMaskFeed(model, out var feed);

            Assert.True(hasFeed);
            Assert.NotNull(feed);
            Assert.Equal(MaskedLatticeSize, feed!.GetLength(0));
            Assert.Equal(MaskedLatticeSize, feed.GetLength(1));

            var flags = model.Layout.Chunks.Single().Masks!.Flags;

            for (var y = 0; y < MaskedLatticeSize; y++)
            {
                for (var x = 0; x < MaskedLatticeSize; x++)
                {
                    var expected = (MaskByteFor(x, y) & (byte)MapMaskFlags.Impassable) != 0;

                    Assert.True(feed[x, y] == expected, $"Feed ({x}, {y}) is {feed[x, y]} for mask byte {MaskByteFor(x, y)}.");
                }
            }

            // Known semantics only: Unbuildable alone must not block movement; only the Impassable bit feeds.
            Assert.False(feed[2, 0]); // Unbuildable
            Assert.True(feed[1, 0]); // Impassable
            Assert.True(feed[3, 0]); // Impassable | Unbuildable
            Assert.False(feed[0, 0]); // None

            var heights = RuntimeBridge.MaterializeHeightMapData(model);
            var navigation = new RuntimeNavigation(feed, new HeightMap(heights));

            Assert.Equal(MaskedLatticeSize, navigation.Graph.Width);
            Assert.Equal(MaskedLatticeSize, navigation.Graph.Height);
            Assert.Equal(Passability.Impassable, navigation.Graph.GetNode(1, 0).Passability);
            Assert.Equal(Passability.Passable, navigation.Graph.GetNode(0, 0).Passability);
        }
        finally
        {
            Game.MapReaderFactory = original;
        }

        Assert.Same(original, Game.MapReaderFactory);
        Assert.IsType<LegacyMapReader>(Game.MapReaderFactory(CreateStubEntry(), CreateEmptyMap()));
    }

    [Fact]
    public void WaypointObjectsStayInMaterializedObjectsWithWaypointSetView()
    {
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(ScriptingWaypoint.CreateWaypointMapObject(new Vector3(100, 200, 0), 3, "Player_1_Start"));
        mapFile.ObjectsList.AddObject(new MapObject(new Vector3(1, 2, 3), 0f, RoadType.None, "AmericaInfantryRanger"));

        var model = new LegacyMapReader(mapFile).Read();
        var objects = RuntimeBridge.MaterializeMapObjects(model);

        // Single spawn array: the waypoint object stays in it; WaypointCollection routing stays in
        // the existing LoadObjects switch, unchanged.
        Assert.True(objects.Length == 2, $"Materialized {objects.Length} objects; expected the waypoint and the regular object.");

        var waypoint = objects[0];

        Assert.Equal(ScriptingWaypoint.ObjectTypeName, waypoint.TypeName);
        Assert.Equal(RoadType.None, waypoint.RoadType);
        Assert.Equal(0u, (uint)waypoint.RoadType);
        Assert.Equal(new Vector3(100, 200, 0), waypoint.Position);
        Assert.Equal(new MapPropertyValue.IntegerValue(3), PropertyValue(waypoint, "waypointID"));
        Assert.Equal(new MapPropertyValue.AsciiStringValue("Player_1_Start"), PropertyValue(waypoint, "waypointName"));

        // Semantic view: the WaypointSet mirrors the starred-object count and is never fed to LoadObjects.
        Assert.Equal(StarredWaypointCount(mapFile), model.Waypoints!.Waypoints.Count);
        Assert.Single(model.Waypoints!.Waypoints);
        Assert.Equal("Player_1_Start", model.Waypoints!.Waypoints[0].Name);
    }

    [Fact]
    public void NonRepresentableTerrainLayoutsRejectHeightMaterialization()
    {
        // Negative Border: HeightMapData stores the border unsigned.
        var negativeBorder = WithLayout(BuildSingleChunkModel(), layout => layout with { Border = -1 });
        AssertHeightGuard(negativeBorder, "TerrainLayout.Border is -1");

        // HeightSectionVersion is stored as ushort in HeightMapData.
        AssertHeightGuard(WithLayout(BuildSingleChunkModel(), layout => layout with { HeightSectionVersion = -1 }), "TerrainLayout.HeightSectionVersion is -1");
        AssertHeightGuard(WithLayout(BuildSingleChunkModel(), layout => layout with { HeightSectionVersion = 70000 }), "TerrainLayout.HeightSectionVersion is 70000");
    }

    [Fact]
    public void HeightsWithoutExactElevationRejectWhileExactOnesMaterialize()
    {
        var inexact = WithCoreSample(BuildSingleChunkModel(), InteriorStoredRow, InteriorStoredColumn, 10.3f);

        var exception = Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeHeightMapData(inexact));
        var diagnostic = Assert.Single(exception.Diagnostics);

        Assert.Equal(MapInterchangeDiagnostic.HeightRepresentable, diagnostic.Code);
        Assert.Contains("has no exact elevation", diagnostic.Message);

        // Exactness is a bridge fact, not a validation finding: the model itself still validates.
        Assert.Empty(MapInterchangeValidator.Validate(inexact));

        var exact = WithCoreSample(BuildSingleChunkModel(), InteriorStoredRow, InteriorStoredColumn, 62.5f);
        var materialized = RuntimeBridge.MaterializeHeightMapData(exact);

        Assert.Equal(100, materialized.Elevations[InteriorStoredRow - 1, InteriorStoredColumn - 1]);
    }

    [Fact]
    public void ObjectCoordinatesMustSurviveFloat32StorageExactly()
    {
        var subFloatPrecision = BuildObjectModel(0.1);

        var exception = Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeMapObjects(subFloatPrecision));
        var diagnostic = Assert.Single(exception.Diagnostics);

        Assert.Equal(MapInterchangeDiagnostic.ObjectRepresentable, diagnostic.Code);
        Assert.Contains("X is 0.1", diagnostic.Message);
        Assert.Empty(MapInterchangeValidator.Validate(subFloatPrecision));

        var exact = BuildObjectModel(0.5);
        var single = Assert.Single(RuntimeBridge.MaterializeMapObjects(exact));

        Assert.Equal(0.5f * (float)UnitConversionPolicy.GeneralsFeetToMeters, single.Position.X);
    }

    [Fact]
    public void RejectedImportsPublishNoPartialResidents()
    {
        var validModel = BuildSingleChunkModel();
        Assert.Empty(MapInterchangeValidator.Validate(validModel));

        var invalidModel = WithCoreSample(validModel, InteriorStoredRow, InteriorStoredColumn, float.NaN);
        Assert.Equal(
            [MapInterchangeDiagnostic.HeightFinite],
            MapInterchangeValidator.Validate(invalidModel).Select(d => d.Code).Distinct().ToArray());

        // Every bridge entry validates first: an invalid model throws before any out value or return.
        bool[,]? feed = null;
        Assert.Throws<MapImportException>(() => RuntimeBridge.TryGetMaskFeed(invalidModel, out feed));
        Assert.Null(feed);
        Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeHeightMapData(invalidModel));
        Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeMapObjects(invalidModel));

        // Models are immutable: after every rejection the untouched input re-validates empty.
        Assert.Empty(MapInterchangeValidator.Validate(validModel));

        // A reader that throws publishes nothing through the production seam either.
        var original = Game.MapReaderFactory;
        Game.MapReaderFactory = (_, _) => new ThrowingReader();

        try
        {
            Assert.Throws<MapImportException>(() => Game.MapReaderFactory(CreateStubEntry(), CreateEmptyMap()).Read());
        }
        finally
        {
            Game.MapReaderFactory = original;
        }

        Assert.Same(original, Game.MapReaderFactory);
    }

    [GameFact(SageGame.CncGenerals)]
    [Trait("Category", "Interactive")]
    public void LoadMapGateRejectsBeforePublishingResidents()
    {
        Platform.Start();

        try
        {
            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                null,
                new Configuration { NoAudio = true },
                null);

            var mapPath = game.ContentManager.GetMapEntry("maps/alpine assault/alpine assault.map") is not null
                ? "maps/alpine assault/alpine assault.map"
                : game.ContentManager.FileSystem.GetFilesInDirectory("maps", "*.map", SearchOption.AllDirectories).First().FilePath;

            var original = Game.MapReaderFactory;
            Game.MapReaderFactory = (_, _) => throw new MapImportException(
            [
                new MapInterchangeDiagnostic(MapInterchangeDiagnostic.NullComponent, "synthetic gate rejection"),
            ]);

            try
            {
                Assert.Null(game.TerrainLogic.HeightMap);

                var exception = Assert.Throws<MapImportException>(
                    () => game.StartSkirmishOrMultiPlayerGame(mapPath, new EchoConnection(), [], seed: 1, isMultiPlayer: false));

                var diagnostic = Assert.Single(exception.Diagnostics);
                Assert.Equal(MapInterchangeDiagnostic.NullComponent, diagnostic.Code);

                // No partial publication: the reader rejection happens before any resident mutates.
                Assert.Null(game.TerrainLogic.HeightMap);
                Assert.Null(game.Scene3D);
            }
            finally
            {
                Game.MapReaderFactory = original;
            }

            Assert.Same(original, Game.MapReaderFactory);
        }
        finally
        {
            Platform.Stop();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    [Trait("Category", "Interactive")]
    public void LoadMapSuccessBuildsSceneFromMaterializedResidents()
    {
        Platform.Start();

        try
        {
            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                null,
                new Configuration { NoAudio = true },
                null);

            var mapPath = game.ContentManager.GetMapEntry("maps/alpine assault/alpine assault.map") is not null
                ? "maps/alpine assault/alpine assault.map"
                : game.ContentManager.FileSystem.GetFilesInDirectory("maps", "*.map", SearchOption.AllDirectories).First().FilePath;

            // Production default path: no factory swap, so the load runs LegacyMapReader for real.
            var factoryBeforeLoad = Game.MapReaderFactory;

            Assert.Null(game.TerrainLogic.HeightMap);
            Assert.Null(game.Scene3D);

            game.StartSkirmishOrMultiPlayerGame(mapPath, new EchoConnection(), [], seed: 1, isMultiPlayer: false);

            Assert.NotNull(game.Scene3D);
            Assert.NotNull(game.TerrainLogic.HeightMap);

            // Heights-only map: no mask feed was published, so terrain falls back to BlendTileData.
            Assert.Null(game.Scene3D.Terrain.ImpassabilityOverride);

            // Navigation is built from the parsed map's BlendTileData; every node must agree with it.
            var blendTileData = game.Scene3D.MapFile.BlendTileData;
            var navigation = game.Scene3D.Navigation;

            Assert.NotNull(navigation);

            var graph = navigation.Graph;

            Assert.Equal(blendTileData.Impassability.GetLength(0), graph.Width);
            Assert.Equal(blendTileData.Impassability.GetLength(1), graph.Height);

            for (var x = 0; x < graph.Width; x++)
            {
                for (var y = 0; y < graph.Height; y++)
                {
                    Assert.True(
                        graph.GetNode(x, y).Passability == (blendTileData.Impassability[x, y] ? Passability.Impassable : Passability.Passable),
                        $"Navigation node ({x}, {y}): BlendTileData says {(blendTileData.Impassability[x, y] ? "impassable" : "passable")}.");
                }
            }

            // Waypoint counts: parse the same entry separately with LegacyMapReader and compare against
            // the scene collection. The collection exposes no public count and duplicate names collapse
            // the name index, so the unique-ID index is its count identity.
            var expectedEntry = game.ContentManager.GetMapEntry(mapPath);
            var expectedMapFile = MapFile.FromFileSystemEntry(expectedEntry);
            var expectedModel = new LegacyMapReader(expectedMapFile, expectedEntry.FilePath).Read();
            var expectedWaypointCount = expectedModel.Waypoints?.Waypoints.Count ?? 0;

            Assert.Equal(expectedWaypointCount, WaypointIdCount(game.Scene3D.Waypoints));

            // The load never touched the reader seam: the default factory is still installed.
            Assert.Same(factoryBeforeLoad, Game.MapReaderFactory);
            Assert.IsType<LegacyMapReader>(Game.MapReaderFactory(CreateStubEntry(), CreateEmptyMap()));
        }
        finally
        {
            Platform.Stop();
        }
    }

    private static int WaypointIdCount(ScriptingWaypoints waypoints)
    {
        var field = waypoints.GetType().GetField("_waypointsByID", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(field);

        return ((System.Collections.Generic.IDictionary<int, ScriptingWaypoint>)field.GetValue(waypoints)!).Count;
    }

    private static void AssertHeightsMatchParsed(MapFile mapFile, HeightMapData materialized)
    {
        var parsed = mapFile.HeightMapData;

        Assert.Equal(parsed.Width, materialized.Width);
        Assert.Equal(parsed.Height, materialized.Height);
        Assert.Equal(parsed.BorderWidth, materialized.BorderWidth);
        Assert.Equal(parsed.Area, materialized.Area);
        Assert.Equal(parsed.Version, materialized.Version);
        Assert.Equal(parsed.MinZ, materialized.MinZ);
        Assert.Equal(parsed.MaxZ, materialized.MaxZ);

        Assert.Equal(parsed.Borders.Length, materialized.Borders.Length);

        for (var i = 0; i < parsed.Borders.Length; i++)
        {
            Assert.True(parsed.Borders[i].Corner1X == materialized.Borders[i].Corner1X, $"Border {i} Corner1X differs.");
            Assert.True(parsed.Borders[i].Corner1Y == materialized.Borders[i].Corner1Y, $"Border {i} Corner1Y differs.");
            Assert.True(parsed.Borders[i].X == materialized.Borders[i].X, $"Border {i} X differs.");
            Assert.True(parsed.Borders[i].Y == materialized.Borders[i].Y, $"Border {i} Y differs.");
        }

        Assert.Equal(parsed.Elevations.GetLength(0), materialized.Elevations.GetLength(0));
        Assert.Equal(parsed.Elevations.GetLength(1), materialized.Elevations.GetLength(1));

        for (var y = 0; y < parsed.Elevations.GetLength(1); y++)
        {
            for (var x = 0; x < parsed.Elevations.GetLength(0); x++)
            {
                Assert.True(
                    parsed.Elevations[x, y] == materialized.Elevations[x, y],
                    $"Elevation ({x}, {y}): parsed {parsed.Elevations[x, y]} != materialized {materialized.Elevations[x, y]}.");
            }
        }
    }

    private static void AssertObjectsMatchParsed(MapFile mapFile, MapObject[] materialized)
    {
        var parsed = mapFile.ObjectsList?.Objects ?? Array.Empty<MapObject>();

        Assert.Equal(parsed.Length, materialized.Length);

        for (var index = 0; index < parsed.Length; index++)
        {
            var expected = parsed[index];
            var actual = materialized[index];

            Assert.True(expected.Position == actual.Position, $"Object {index} ({expected.TypeName}): position {expected.Position} != {actual.Position}.");
            Assert.True(expected.Angle == actual.Angle, $"Object {index} ({expected.TypeName}): angle {expected.Angle} != {actual.Angle}.");
            Assert.True((uint)expected.RoadType == (uint)actual.RoadType, $"Object {index} ({expected.TypeName}): road type {(uint)expected.RoadType} != {(uint)actual.RoadType}.");
            Assert.Equal(expected.TypeName, actual.TypeName);

            var expectedProperties = ToPropertyValues(expected.Properties);
            var actualProperties = ToPropertyValues(actual.Properties);

            Assert.Equal(expectedProperties.Count, actualProperties.Count);

            foreach (var (name, expectedValue) in expectedProperties)
            {
                var found = actualProperties.TryGetValue(name, out var actualValue);

                Assert.True(
                    found && Equals(expectedValue, actualValue),
                    $"Object {index} ({expected.TypeName}) property '{name}': expected {expectedValue}, actual {(found ? actualValue!.ToString() : "<missing>")}.");
            }
        }
    }

    private static void AssertWaypointSetMatchesStarredObjects(MapInterchange model, MapFile mapFile)
    {
        var starred = StarredWaypointCount(mapFile);
        var waypoints = model.Waypoints?.Waypoints.Count ?? 0;

        Assert.True(waypoints == starred, $"WaypointSet holds {waypoints} waypoints; ObjectsList carries {starred} starred waypoint objects.");
    }

    private static void AssertHeightGuard(MapInterchange model, string messageFragment)
    {
        Assert.Empty(MapInterchangeValidator.Validate(model));

        var exception = Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeHeightMapData(model));
        var diagnostic = Assert.Single(exception.Diagnostics);

        Assert.Equal(MapInterchangeDiagnostic.HeightRepresentable, diagnostic.Code);
        Assert.Contains(messageFragment, diagnostic.Message);
    }

    private static int StarredWaypointCount(MapFile mapFile)
    {
        return (mapFile.ObjectsList?.Objects ?? Array.Empty<MapObject>())
            .Count(mapObject => mapObject.TypeName == ScriptingWaypoint.ObjectTypeName
                && mapObject.Properties.GetPropOrNull("waypointName")?.Value is string);
    }

    private static MapPropertyValue PropertyValue(MapObject mapObject, string name)
    {
        var property = mapObject.Properties[name];

        return property.Key.PropertyType switch
        {
            AssetPropertyType.Boolean => new MapPropertyValue.BooleanValue((bool)property.Value!),
            AssetPropertyType.Integer => new MapPropertyValue.IntegerValue((int)property.Value!),
            AssetPropertyType.RealNumber => new MapPropertyValue.RealValue((float)property.Value!),
            AssetPropertyType.AsciiString => new MapPropertyValue.AsciiStringValue((string)property.Value!),
            AssetPropertyType.UnicodeString => new MapPropertyValue.UnicodeStringValue((string)property.Value!),
            _ => throw new InvalidOperationException($"Property '{name}' carries unsupported type {property.Key.PropertyType}."),
        };
    }

    private static Dictionary<string, MapPropertyValue> ToPropertyValues(AssetPropertyCollection properties)
    {
        var result = new Dictionary<string, MapPropertyValue>(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            result[property.Key.Name] = property.Key.PropertyType switch
            {
                AssetPropertyType.Boolean => new MapPropertyValue.BooleanValue((bool)property.Value!),
                AssetPropertyType.Integer => new MapPropertyValue.IntegerValue((int)property.Value!),
                AssetPropertyType.RealNumber => new MapPropertyValue.RealValue((float)property.Value!),
                AssetPropertyType.AsciiString => new MapPropertyValue.AsciiStringValue((string)property.Value!),
                AssetPropertyType.UnicodeString => new MapPropertyValue.UnicodeStringValue((string)property.Value!),
                _ => throw new InvalidOperationException($"Property '{property.Key.Name}' carries unsupported type {property.Key.PropertyType}."),
            };
        }

        return result;
    }

    private static MapInterchange BuildSingleChunkModel(bool masks = false, PlacedObjectSet? objects = null)
    {
        var heights = new float[StoredWidth * StoredWidth];

        for (var row = 0; row < StoredWidth; row++)
        {
            for (var column = 0; column < StoredWidth; column++)
            {
                // One value function over lattice coordinates keeps seams and aprons consistent by
                // construction; x + 2 keeps the map-edge apron values non-negative.
                heights[(row * StoredWidth) + column] = LatticeElevation(column - 1, row - 1) * LegacyVerticalScale;
            }
        }

        var chunk = new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(heights),
            null,
            masks ? new MaskLayer(BuildMaskFlags()) : null,
            null);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            MaskedLatticeSize,
            MaskedLatticeSize,
            1,
            1,
            0,
            null!, // Legacy sources carry no geospatial transform; absence is recorded as null.
            MapNodataPolicy.Fail,
            3,
            (uint)(MaskedLatticeSize * MaskedLatticeSize),
            [new MapTerrainBorder(0, 0, (uint)(MaskedLatticeSize - 1), (uint)(MaskedLatticeSize - 1))],
            [chunk]);

        var bytes = new byte[] { 1, 2, 3, 4 };

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("synthetic", MapSourceKind.Generated, null),
            layout,
            new BlendModel([]),
            objects,
            null,
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            [new RetainedSection("GlobalVersion", 8, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)))]);
    }

    private static MapInterchange BuildObjectModel(double x)
    {
        var objects = new PlacedObjectSet(
        [
            new PlacedObject(
                "object-1",
                new MapQuantity(x, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(20, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ),
                0.25,
                new AssetReference("legacy", "AmericaInfantryRanger"),
                0u,
                new Dictionary<string, MapPropertyValue>()),
        ]);

        return BuildSingleChunkModel(objects: objects);
    }

    private static byte[] BuildMaskFlags()
    {
        var flags = new byte[MaskedLatticeSize * MaskedLatticeSize];

        for (var i = 0; i < flags.Length; i++)
        {
            // Cycles None, Impassable, Unbuildable, Impassable|Unbuildable; MaskByteFor mirrors it.
            flags[i] = (byte)(i % 4);
        }

        return flags;
    }

    private static byte MaskByteFor(int x, int y) => (byte)(((y * MaskedLatticeSize) + x) % 4);

    [Fact]
    public void Aq3_ElevationIntegersRoundTripUnchanged()
    {
        // 8-bit elevation range: 0..255
        const float scale8Bit = 0.625f;
        for (var rawElevation = 0; rawElevation <= 255; rawElevation++)
        {
            var heightSample = rawElevation * scale8Bit;
            var recoveredElevation = (ushort)MathF.Round(heightSample / scale8Bit);
            Assert.Equal(rawElevation, recoveredElevation);
        }

        // 16-bit elevation range samples up to 65535
        const float scale16Bit = 0.0390625f;
        int[] samples16Bit = [0, 1, 2, 10, 100, 255, 256, 500, 1000, 5000, 16384, 32767, 65535];
        foreach (var rawElevation in samples16Bit)
        {
            var heightSample = rawElevation * scale16Bit;
            var recoveredElevation = (ushort)MathF.Round(heightSample / scale16Bit);
            Assert.Equal(rawElevation, recoveredElevation);
        }

        // Model round-trip through RuntimeBridge:
        var model = BuildSingleChunkModel();
        var materialized = RuntimeBridge.MaterializeHeightMapData(model);
        Assert.NotNull(materialized);
        Assert.Equal(LatticeElevation(InteriorStoredColumn - 1, InteriorStoredRow - 1), materialized.Elevations[InteriorStoredRow - 1, InteriorStoredColumn - 1]);
    }

    private static int LatticeElevation(int x, int y) => (((x + 2) * 7) + ((y + 2) * 13)) % 100;

    private static MapInterchange WithLayout(MapInterchange model, Func<TerrainLayout, TerrainLayout> mutate)
    {
        return model with { Layout = mutate(model.Layout) };
    }

    private static MapInterchange WithCoreSample(MapInterchange model, int storedRow, int storedColumn, float value)
    {
        var chunk = model.Layout.Chunks.Single();
        var samples = chunk.Heights.Samples.ToArray();
        samples[(storedRow * StoredWidth) + storedColumn] = value;

        return model with { Layout = model.Layout with { Chunks = [chunk with { Heights = new HeightLayer(samples) }] } };
    }

    private static MapFile CreateEmptyMap() => MapFile.CreateEmpty(150, 150, 30, 100);

    private static FileSystemEntry CreateStubEntry() => new(new DiskFileSystem("."), "synthetic.map", 0, () => Stream.Null);

    private static Stream? OpenOrNull(FileSystemEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private sealed class FixedModelReader(MapInterchange model) : IMapReader
    {
        public MapInterchange Model { get; } = model;

        public MapInterchange Read() => Model;
    }

    private sealed class ThrowingReader : IMapReader
    {
        public MapInterchange Read() => throw new MapImportException(
        [
            new MapInterchangeDiagnostic(MapInterchangeDiagnostic.NullComponent, "synthetic rejection"),
        ]);
    }
}
