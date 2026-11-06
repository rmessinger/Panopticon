#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources;

/// <summary>
/// WI-H2-03: the legacy adapter through the reader-source seam. Synthetic cases use
/// MapFile.CreateEmpty (playable 150, border 30, lattice 211) and boot no game files; the
/// corpus case reads installed maps and passes vacuously without an installation, mirroring
/// MapFileTests.
/// </summary>
public sealed class LegacyMapReaderTests
{
    private const uint PlayableWidth = 150;

    private const uint PlayableHeight = 150;

    private const uint BorderWidth = 30;

    private const ushort InitialHeight = 100;

    private const int CorpusValidatingTarget = 3;

    private const int ChunkIntervals = MapChunkGeometry.IntervalsPerChunkAxis;

    private readonly ITestOutputHelper _output;

    public LegacyMapReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CreateEmptyMapProducesValidatingModel()
    {
        var model = new LegacyMapReader(CreateEmptyMap()).Read();

        Assert.Empty(MapInterchangeValidator.Validate(model));

        // Identity: default label, legacy source kind, and no Ra3 GlobalVersion on a Generals-era map.
        Assert.Equal("legacy", model.Identity.Name);
        Assert.Equal(MapSourceKind.LegacyMap, model.Identity.SourceKind);
        Assert.Null(model.Identity.LegacyGlobalVersion);

        // Layout: declared whole-map lattice, partial edge chunks, spacing, border, nodata policy.
        Assert.Equal(211, model.Layout.LatticeWidth);
        Assert.Equal(211, model.Layout.LatticeHeight);
        Assert.Equal(2, model.Layout.ChunksX);
        Assert.Equal(2, model.Layout.ChunksY);
        Assert.Equal((int)BorderWidth, model.Layout.Border);
        Assert.Equal(new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy), model.Layout.SampleSpacing);
        Assert.Null(model.Layout.GeospatialTransform);
        Assert.Equal(MapNodataPolicy.Fail, model.Layout.NodataPolicy);

        // Chunk math: chunk 1 core width = 211 - 128 = 83, stored width 85; full chunks stay 131 x 131.
        Assert.Equal(131 * 131, Heights(model, 0, 0).Samples.Count);
        Assert.Equal(85 * 131, Heights(model, 1, 0).Samples.Count);
        Assert.Equal(131 * 85, Heights(model, 0, 1).Samples.Count);
        Assert.Equal(85 * 85, Heights(model, 1, 1).Samples.Count);

        // Heights-only chunks: legacy sources carry no palette, mask, or imagery layers.
        Assert.All(model.Layout.Chunks, chunk => Assert.Null(chunk.Materials));
        Assert.All(model.Layout.Chunks, chunk => Assert.Null(chunk.Masks));
        Assert.All(model.Layout.Chunks, chunk => Assert.Null(chunk.Imagery));
        Assert.Empty(model.Blend.Palette);

        // Retained sections: WorldInfo and BlendTileData at least, digests recomputing from bytes.
        Assert.Contains(model.RetainedSections, section => section.SectionName == "WorldInfo");
        Assert.Contains(model.RetainedSections, section => section.SectionName == "BlendTileData");
        Assert.Contains(model.RetainedSections, section => section.SectionName == "SidesList");
        Assert.Contains(model.RetainedSections, section => section.SectionName == "WaypointsList");
        Assert.Equal(["BlendTileData", "WorldInfo", "SidesList", "GlobalLighting", "WaypointsList"], model.RetainedSections.Select(section => section.SectionName).ToArray());
        Assert.All(model.RetainedSections, section => Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(section.Bytes.Span)), section.Sha256));

        // Typed homes for a new empty map: empty placements and waypoint sets, no provenance.
        Assert.NotNull(model.Objects);
        Assert.Empty(model.Objects!.Objects);
        Assert.NotNull(model.Waypoints);
        Assert.Empty(model.Waypoints!.Waypoints);
        Assert.Null(model.Provenance.SourceSchemaVersion);
        Assert.Empty(model.Provenance.Sources);
    }

    [Fact]
    public void HeightsEqualElevationTimesVerticalScale()
    {
        var mapFile = CreateEmptyMap();
        var elevations = mapFile.HeightMapData.Elevations;
        var width = elevations.GetLength(0);
        var height = elevations.GetLength(1);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                elevations[x, y] = (ushort)((x * 7 + y * 13) % 1000);
            }
        }

        var model = new LegacyMapReader(mapFile).Read();
        var scale = mapFile.HeightMapData.VerticalScale;

        Assert.Equal(0.625f, scale);

        // Interior core sample of chunk (0, 0): core (5, 9) at stored column 6, row 10.
        Assert.Equal(elevations[5, 9] * scale, StoredHeight(model, 0, 0, 6, 10));

        // East apron of chunk (0, 0) at lattice (129, 40): the real neighbour lattice point.
        Assert.Equal(elevations[129, 40] * scale, StoredHeight(model, 0, 0, 130, 41));

        // North apron of chunk (0, 0) at lattice (100, 129): the real neighbour lattice point.
        Assert.Equal(elevations[100, 129] * scale, StoredHeight(model, 0, 0, 101, 130));

        // Map-edge aprons follow the clamp policy: lattice (-1, -1) clamps to (0, 0).
        Assert.Equal(elevations[0, 0] * scale, StoredHeight(model, 0, 0, 0, 0));

        // The northeast apron of chunk (1, 1) sits at lattice (211, 211), clamped to (210, 210).
        Assert.Equal(elevations[210, 210] * scale, StoredHeight(model, 1, 1, 84, 84));

        // The shared seam point at lattice (128, 40) reads identically from both neighbouring chunks.
        Assert.Equal(elevations[128, 40] * scale, StoredHeight(model, 0, 0, 129, 41));
        Assert.Equal(elevations[128, 40] * scale, StoredHeight(model, 1, 0, 1, 41));
    }

    [Fact]
    public void ObjectsMapToPlacedObjectSet()
    {
        var mapFile = CreateEmptyMap();
        var mapObject = new MapObject(new Vector3(10, 20, 30), 1.5f, RoadType.None, "AmericaTankCrusher");
        mapObject.Properties.AddInteger("hitpoints", 400);
        mapObject.Properties.AddAsciiString("caption", "crusher");
        mapFile.ObjectsList.AddObject(mapObject);

        var model = new LegacyMapReader(mapFile).Read();
        var placed = Assert.Single(model.Objects!.Objects);

        Assert.Equal("obj-0", placed.StableId);
        Assert.Equal(10, placed.X.Value);
        Assert.Equal(MapQuantityUnit.GameUnit, placed.X.Unit);
        Assert.Equal(MapQuantityFrame.LegacyEngineXy, placed.X.Frame);
        Assert.Equal(20, placed.Y.Value);
        Assert.Equal(MapQuantityFrame.LegacyEngineXy, placed.Y.Frame);
        Assert.Equal(30, placed.Z.Value);
        Assert.Equal(MapQuantityFrame.LegacyEngineZ, placed.Z.Frame);
        Assert.Equal(1.5, placed.OrientationRadians);
        Assert.Equal(new AssetReference("legacy", "AmericaTankCrusher"), placed.Type);
        Assert.Equal(2, placed.LegacyProperties.Count);
        Assert.Equal(new MapPropertyValue.IntegerValue(400), placed.LegacyProperties["hitpoints"]);
        Assert.Equal(new MapPropertyValue.AsciiStringValue("crusher"), placed.LegacyProperties["caption"]);
    }

    [Fact]
    public void WaypointObjectsMapToWaypointSet()
    {
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(Panopticon.Scripting.Waypoint.CreateWaypointMapObject(new Vector3(100, 200, 0), 1, "Player_1_Start"));

        var model = new LegacyMapReader(mapFile).Read();
        var waypoint = Assert.Single(model.Waypoints!.Waypoints);

        // StableId is positional and Name carries the legacy waypoint name; legacy waypoints carry no IDs.
        Assert.Equal("wp-0", waypoint.StableId);
        Assert.Equal("Player_1_Start", waypoint.Name);
        Assert.Equal(100, waypoint.X.Value);
        Assert.Equal(MapQuantityFrame.LegacyEngineXy, waypoint.X.Frame);
        Assert.Equal(200, waypoint.Y.Value);
        Assert.Equal(0, waypoint.Z.Value);
        Assert.Equal(MapQuantityFrame.LegacyEngineZ, waypoint.Z.Frame);
    }

    [Fact]
    public void LegacyPropertyKindsRoundTripAsTypedValues()
    {
        var mapFile = CreateEmptyMap();
        var mapObject = new MapObject(new Vector3(1, 2, 3), 0, RoadType.None, "AmericaTankCrusher");
        mapObject.Properties.AddBoolean("enabled", true);
        mapObject.Properties.AddInteger("hitpoints", -400);
        mapObject.Properties.AddReal("scale", 0.625f);
        mapObject.Properties.AddAsciiString("caption", "crusher");
        mapObject.Properties.AddUnicodeString("displayName", "Brücke 日本語 ☃");
        mapFile.ObjectsList.AddObject(mapObject);

        var model = new LegacyMapReader(RoundTrip(mapFile)).Read();
        var properties = Assert.Single(model.Objects!.Objects).LegacyProperties;

        Assert.Equal(5, properties.Count);
        Assert.Equal(new MapPropertyValue.BooleanValue(true), properties["enabled"]);
        Assert.Equal(new MapPropertyValue.IntegerValue(-400), properties["hitpoints"]);
        Assert.Equal(new MapPropertyValue.RealValue(0.625f), properties["scale"]);
        Assert.Equal(new MapPropertyValue.AsciiStringValue("crusher"), properties["caption"]);
        Assert.Equal(new MapPropertyValue.UnicodeStringValue("Brücke 日本語 ☃"), properties["displayName"]);
    }

    [Fact]
    public void RoadTypeBitsPassThroughUnmasked()
    {
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(new MapObject(Vector3.Zero, 0, RoadType.TightCurve, "RoadTightCurve"));
        mapFile.ObjectsList.AddObject(new MapObject(Vector3.Zero, 0, RoadType.Start | RoadType.Angled | RoadType.Unknown3 | RoadType.Unknown4, "RoadCombined"));

        var model = new LegacyMapReader(RoundTrip(mapFile)).Read();
        var placed = model.Objects!.Objects;

        Assert.Equal(64u, placed[0].RoadType);
        Assert.Equal(2u | 8u | 256u | 512u, placed[1].RoadType);
    }

    [Fact]
    public void TerrainBordersAreaAndVersionCarryVerbatim()
    {
        var mapFile = CreateEmptyMap();

        var model = new LegacyMapReader(mapFile).Read();

        Assert.Equal(new MapTerrainBorder(0, 0, 210, 210), Assert.Single(model.Layout.TerrainBorders));
        Assert.Equal(211u * 211u, model.Layout.MapArea);
        Assert.Equal((int)mapFile.HeightMapData.Version, model.Layout.HeightSectionVersion);
    }

    [Fact]
    public void UnknownPropertyKindRejectsImport()
    {
        var mapFile = CreateEmptyMap();
        var mapObject = new MapObject(Vector3.Zero, 0, RoadType.None, "AmericaTankCrusher");
        mapObject.Properties.Add(new AssetProperty("legacyUnknown", AssetPropertyType.Unknown, "payload"));
        mapFile.ObjectsList.AddObject(mapObject);

        var exception = Assert.Throws<MapImportException>(() => new LegacyMapReader(mapFile).Read());

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(MapInterchangeDiagnostic.PropertyType, diagnostic.Code);
    }

    [Fact]
    public void ObjectsKeepEnumerationOrderWithPositionalIds()
    {
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(new MapObject(new Vector3(1, 0, 0), 0, RoadType.None, "TypeA"));
        mapFile.ObjectsList.AddObject(Panopticon.Scripting.Waypoint.CreateWaypointMapObject(new Vector3(2, 0, 0), 1, "Start"));
        mapFile.ObjectsList.AddObject(new MapObject(new Vector3(3, 0, 0), 0, RoadType.None, "TypeB"));

        var model = new LegacyMapReader(mapFile).Read();
        var placed = model.Objects!.Objects;

        Assert.Equal(["obj-0", "obj-1", "obj-2"], placed.Select(item => item.StableId).ToArray());
        Assert.Equal(["TypeA", Panopticon.Scripting.Waypoint.ObjectTypeName, "TypeB"], placed.Select(item => item.Type.Name).ToArray());
        Assert.Equal([1, 2, 3], placed.Select(item => item.X.Value).ToArray());
    }

    [Fact]
    public void SourceLabelBecomesIdentityName()
    {
        var model = new LegacyMapReader(CreateEmptyMap(), "maps/deepdivide/deepdivide.map").Read();

        Assert.Equal("maps/deepdivide/deepdivide.map", model.Identity.Name);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void IsSupportedSidesListVersionGatesAtTwo(int version, bool supported)
    {
        Assert.Equal(supported, LegacyMapReader.IsSupportedSidesListVersion(version));
    }

    [Fact]
    public void SidesListBelowVersionTwoRejectsImport()
    {
        var mapFile = CreateEmptyMap();
        mapFile.SidesList.Version = 1;

        var exception = Assert.Throws<MapImportException>(() => new LegacyMapReader(mapFile).Read());

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(MapInterchangeDiagnostic.SidesListVersion, diagnostic.Code);
    }

    [Fact]
    public void AbsentSidesListIsNeitherRejectedNorRetained()
    {
        var mapFile = CreateEmptyMap();
        mapFile.SidesList = null;

        var model = new LegacyMapReader(mapFile).Read();

        Assert.Empty(MapInterchangeValidator.Validate(model));
        Assert.DoesNotContain(model.RetainedSections, section => section.SectionName == "SidesList");
    }

    [Fact]
    public void DuplicateWaypointNamesGetPositionalIdsAndValidate()
    {
        // Legacy waypoint names are not unique; stable IDs are positional, so duplicate names
        // yield distinct stable IDs and the model validates.
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(Panopticon.Scripting.Waypoint.CreateWaypointMapObject(new Vector3(1, 2, 3), 1, "Start"));
        mapFile.ObjectsList.AddObject(Panopticon.Scripting.Waypoint.CreateWaypointMapObject(new Vector3(4, 5, 6), 2, "Start"));
        mapFile.ObjectsList.AddObject(Panopticon.Scripting.Waypoint.CreateWaypointMapObject(new Vector3(7, 8, 9), 3, "Finish"));

        var model = new LegacyMapReader(mapFile).Read();

        Assert.Empty(MapInterchangeValidator.Validate(model));
        Assert.Equal(["wp-0", "wp-1", "wp-2"], model.Waypoints!.Waypoints.Select(waypoint => waypoint.StableId).ToArray());
        Assert.Equal(["Start", "Start", "Finish"], model.Waypoints!.Waypoints.Select(waypoint => waypoint.Name).ToArray());
        Assert.Equal([1, 4, 7], model.Waypoints!.Waypoints.Select(waypoint => waypoint.X.Value).ToArray());
    }

    [Fact]
    public void ValidatorDiagnosticsSurfaceOnMapImportException()
    {
        // Non-finite object positions fail model validation; the exception carries the validator's
        // diagnostics verbatim.
        var mapFile = CreateEmptyMap();
        mapFile.ObjectsList.AddObject(new MapObject(new Vector3(float.NaN, 20, 30), 0, RoadType.None, "AmericaTankCrusher"));

        var exception = Assert.Throws<MapImportException>(() => new LegacyMapReader(mapFile).Read());

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(MapInterchangeDiagnostic.QuantityFinite, diagnostic.Code);
    }

    [Fact]
    public void InstalledMapsProduceValidatingModels()
    {
        var filesSeen = 0;
        var validated = 0;
        var rejected = 0;
        var unparseable = 0;
        var unreadable = 0;
        MapInterchange? firstModel = null;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            filesSeen++;

            if (validated >= CorpusValidatingTarget)
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
                validated++;
                firstModel ??= model;

                if (mapFile.WaypointsList is not null)
                {
                    Assert.Contains(model.RetainedSections, section => section.SectionName == "WaypointsList");
                }

                _output.WriteLine(
                    $"{entry.FilePath}: lattice {model.Layout.LatticeWidth}x{model.Layout.LatticeHeight}, chunks {model.Layout.ChunksX}x{model.Layout.ChunksY}, " +
                    $"objects {model.Objects!.Objects.Count}, waypoints {model.Waypoints!.Waypoints.Count}, retained {model.RetainedSections.Count}.");
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

        _output.WriteLine($"Corpus import: {validated} validating, {rejected} rejected, {unparseable} unparseable, {unreadable} unreadable.");

        Assert.Equal(CorpusValidatingTarget, validated);
        Assert.NotNull(firstModel);
        Assert.NotEmpty(firstModel!.RetainedSections);
        Assert.NotNull(firstModel.Objects);
        Assert.NotNull(firstModel.Waypoints);
    }

    [Fact]
    public void AlpineAssaultValidates()
    {
        // Alpine Assault carries duplicate legacy waypoint names; positional stable IDs must let it
        // validate where name-derived IDs rejected it.
        var found = false;

        InstalledFilesTestData.ReadFiles(".map", _output, entry =>
        {
            if (!entry.FilePath.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            found = true;

            using var entryStream = OpenOrNull(entry);

            Assert.NotNull(entryStream);

            using var mapStream = MapFile.Decompress(entryStream!);
            var mapFile = MapFile.FromStream(mapStream);
            var model = new LegacyMapReader(mapFile, entry.FilePath).Read();

            Assert.Empty(MapInterchangeValidator.Validate(model));

            if (mapFile.WaypointsList is not null)
            {
                Assert.Contains(model.RetainedSections, section => section.SectionName == "WaypointsList");
            }

            var names = model.Waypoints!.Waypoints.Select(waypoint => waypoint.Name).ToArray();
            var ids = model.Waypoints!.Waypoints.Select(waypoint => waypoint.StableId).ToArray();

            _output.WriteLine(
                $"{entry.FilePath}: lattice {model.Layout.LatticeWidth}x{model.Layout.LatticeHeight}, chunks {model.Layout.ChunksX}x{model.Layout.ChunksY}, " +
                $"objects {model.Objects!.Objects.Count}, waypoints {model.Waypoints.Waypoints.Count}, retained {model.RetainedSections.Count}, " +
                $"waypoint ids [{string.Join(", ", ids)}], duplicate names {names.Length - names.Distinct().Count()}.");
        });

        if (!found)
        {
            _output.WriteLine("No Alpine Assault map in the installed corpus; skipped.");
        }
    }

    private static MapFile CreateEmptyMap() => MapFile.CreateEmpty(PlayableWidth, PlayableHeight, BorderWidth, InitialHeight);

    private static MapFile RoundTrip(MapFile mapFile)
    {
        using var written = new MemoryStream();
        mapFile.WriteTo(written);
        return MapFile.FromStream(new MemoryStream(written.ToArray()));
    }

    private static HeightLayer Heights(MapInterchange model, int cx, int cy)
    {
        return model.Layout.Chunks.Single(chunk => chunk.Key.Cx == cx && chunk.Key.Cy == cy).Heights;
    }

    private static float StoredHeight(MapInterchange model, int cx, int cy, int storedColumn, int storedRow)
    {
        var chunk = Heights(model, cx, cy);
        var coreWidth = Math.Min(MapChunkGeometry.CorePointsPerAxis, model.Layout.LatticeWidth - (cx * ChunkIntervals));
        var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);

        return chunk.Samples[(storedRow * storedWidth) + storedColumn];
    }

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
}
