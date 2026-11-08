#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Panopticon.Content.Importing.Maps;
using Panopticon.Data.Map;
using Panopticon.Logic.Map;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Reader-source adapter from a parsed legacy map to <see cref="MapInterchange"/>. One disposition per
/// audited section, in the legacy writer's dispatch order: HeightMapData, ObjectsList, and GlobalVersion
/// land in their typed homes; every other section present and non-null is retained opaque (SidesList only
/// at version 2 or above; below 2 the import fails, because those versions cannot round-trip).
/// </summary>
/// <remarks>
/// Adapter conventions, per the H2 design:
/// Legacy engine XY and Z are read as GameUnit without conversion. Elevations scale by the section's
/// VerticalScale (0.0390625 at 16 bit, 0.625 at 8 bit) into float GameUnit heights; no other conversion
/// runs. Lattice spacing is declared as 10 game units per sample. Stored heights are row-major with row 0
/// the southern apron and lattice y increasing north (adapter orientation rule). An apron sample holds the
/// real neighbour lattice point's value where it exists, else the clamped nearest core sample at the map
/// edge (adapter clamp policy). Legacy sources carry no palette or mask layers, so chunks are heights-only
/// and <see cref="BlendModel"/> stays empty. Placed objects keep ObjectsList enumeration order, waypoint-type
/// objects included, with stable IDs "obj-{index}" (legacy objects carry no IDs). Property dictionaries are
/// typed through <see cref="MapPropertyValue"/>; RoadType bits pass through unmasked; MapObject.Angle is
/// radians and passes through. Waypoint stable IDs are positional "wp-{index}" over the starred waypoint
/// objects in enumeration order (legacy waypoints carry no IDs); legacy waypoint names are not unique, so Name
/// carries the legacy name and duplicate names validate. The WaypointsList section carries only path
/// records and is retained opaque. Terrain borders, MapArea, and HeightSectionVersion are carried verbatim
/// from HeightMapData with no defaults. Retained bytes are the section
/// serialized exactly as the legacy writer frames it (version, back-patched size, payload); the
/// container-level section name index is excluded, and nested name indices come from a fresh per-section
/// name table. Legacy sources carry no geospatial transform or source digests.
/// </remarks>
public sealed class LegacyMapReader : IMapReader
{
    private const string DefaultSourceLabel = "legacy";

    private const string ObjectStableIdPrefix = "obj-";

    private const string WaypointStableIdPrefix = "wp-";

    private const string WaypointNameProperty = "waypointName";

    private const double LatticeSpacingGameUnits = 10;

    private const int IntervalsPerChunkAxis = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int CorePointsPerAxis = MapChunkGeometry.CorePointsPerAxis;

    private readonly MapFile _mapFile;

    private readonly string _sourceLabel;

    public LegacyMapReader(MapFile mapFile, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(mapFile);
        _mapFile = mapFile;
        _sourceLabel = sourceLabel ?? DefaultSourceLabel;
    }

    /// <summary>SidesList versions below 2 cannot round-trip through the legacy reader and reject the import.</summary>
    internal static bool IsSupportedSidesListVersion(int version) => version >= 2;

    public MapInterchange Read()
    {
        if (_mapFile.SidesList is { } sidesList && !IsSupportedSidesListVersion(sidesList.Version))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.SidesListVersion,
                    $"SidesList version {sidesList.Version} cannot round-trip through the legacy reader; version 2 or above is required."),
            ]);
        }

        var model = new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            BuildIdentity(),
            BuildLayout(),
            new BlendModel([]),
            BuildObjects(),
            BuildWaypoints(),
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            BuildRetainedSections());

        var diagnostics = MapInterchangeValidator.Validate(model);

        if (diagnostics.Count > 0)
        {
            throw new MapImportException(diagnostics);
        }

        return model;
    }

    private MapIdentity BuildIdentity() => new(_sourceLabel, MapSourceKind.LegacyMap, _mapFile.GlobalVersion?.Version);

    private TerrainLayout BuildLayout()
    {
        var heights = _mapFile.HeightMapData;
        var latticeWidth = (int)heights.Width;
        var latticeHeight = (int)heights.Height;
        var chunksX = ChunksFor(latticeWidth);
        var chunksY = ChunksFor(latticeHeight);
        var chunks = new List<Chunk>(chunksX * chunksY);

        for (var cy = 0; cy < chunksY; cy++)
        {
            for (var cx = 0; cx < chunksX; cx++)
            {
                chunks.Add(BuildChunk(heights, cx, cy));
            }
        }

        return new TerrainLayout(
            new MapQuantity(LatticeSpacingGameUnits, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticeWidth,
            latticeHeight,
            chunksX,
            chunksY,
            (int)heights.BorderWidth,
            null!, // Legacy sources carry no geospatial transform; absence is recorded as null.
            MapNodataPolicy.Fail,
            (int)heights.Version,
            heights.Area,
            heights.Borders.Select(border => new MapTerrainBorder(border.Corner1X, border.Corner1Y, border.X, border.Y)).ToArray(),
            chunks);
    }

    private static int ChunksFor(int latticePoints) => (latticePoints - 1 + IntervalsPerChunkAxis - 1) / IntervalsPerChunkAxis;

    private static Chunk BuildChunk(HeightMapData heights, int cx, int cy)
    {
        var latticeWidth = (int)heights.Width;
        var latticeHeight = (int)heights.Height;
        var coreWidth = CoreExtent(latticeWidth, cx);
        var coreHeight = CoreExtent(latticeHeight, cy);
        var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var storedHeight = coreHeight + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var samples = new float[storedWidth * storedHeight];
        var scale = heights.VerticalScale;
        var originX = cx * IntervalsPerChunkAxis;
        var originY = cy * IntervalsPerChunkAxis;

        for (var row = 0; row < storedHeight; row++)
        {
            // Row 0 is the southern apron; lattice y increases north. Outside the map, the nearest
            // core sample is clamped (adapter clamp policy for map-edge aprons).
            var ly = Math.Clamp(originY + row - 1, 0, latticeHeight - 1);

            for (var column = 0; column < storedWidth; column++)
            {
                var lx = Math.Clamp(originX + column - 1, 0, latticeWidth - 1);
                samples[(row * storedWidth) + column] = heights.Elevations[lx, ly] * scale;
            }
        }

        return new Chunk(new ChunkKey(cx, cy), new HeightLayer(samples), null, null, null);
    }

    private static int CoreExtent(int latticePoints, int chunkIndex) => Math.Min(CorePointsPerAxis, latticePoints - (chunkIndex * IntervalsPerChunkAxis));

    private PlacedObjectSet? BuildObjects()
    {
        var objectsList = _mapFile.ObjectsList;

        if (objectsList is null)
        {
            return null;
        }

        var placed = new List<PlacedObject>(objectsList.Objects.Length);

        for (var index = 0; index < objectsList.Objects.Length; index++)
        {
            var mapObject = objectsList.Objects[index];
            placed.Add(new PlacedObject(
                $"{ObjectStableIdPrefix}{index}",
                new MapQuantity(mapObject.Position.X, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(mapObject.Position.Y, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(mapObject.Position.Z, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ),
                mapObject.Angle,
                new AssetReference(DefaultSourceLabel, mapObject.TypeName),
                (uint)mapObject.RoadType,
                ConvertProperties(mapObject.Properties, mapObject.TypeName, index)));
        }

        return new PlacedObjectSet(placed);
    }

    private static IReadOnlyDictionary<string, MapPropertyValue> ConvertProperties(AssetPropertyCollection properties, string objectTypeName, int objectIndex)
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
                _ => throw new MapImportException(
                    [
                        new MapInterchangeDiagnostic(
                            MapInterchangeDiagnostic.PropertyType,
                            $"ObjectsList object {objectIndex} ({objectTypeName}) carries property '{property.Key.Name}' of unsupported type {property.Key.PropertyType}."),
                    ]),
            };
        }

        return result;
    }

    private WaypointSet? BuildWaypoints()
    {
        if (_mapFile.WaypointsList is null)
        {
            return null;
        }

        // Named waypoints are the starred waypoint objects in ObjectsList; the WaypointsList section
        // itself carries only path start and end IDs and is retained opaque. Legacy waypoint names are
        // not unique, so stable IDs are positional over the waypoint enumeration order.
        var waypoints = new List<Waypoint>();

        foreach (var mapObject in _mapFile.ObjectsList?.Objects ?? [])
        {
            if (mapObject.TypeName != Panopticon.Scripting.Waypoint.ObjectTypeName)
            {
                continue;
            }

            if (mapObject.Properties.GetPropOrNull(WaypointNameProperty)?.Value is not string name)
            {
                continue;
            }

            waypoints.Add(new Waypoint(
                $"{WaypointStableIdPrefix}{waypoints.Count}",
                name,
                new MapQuantity(mapObject.Position.X, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(mapObject.Position.Y, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(mapObject.Position.Z, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ)));
        }

        return new WaypointSet(waypoints);
    }

    private IReadOnlyList<RetainedSection> BuildRetainedSections()
    {
        var sections = new List<RetainedSection>();
        var mapHasAssetList = _mapFile.AssetList is not null;

        if (_mapFile.AssetList is { } assetList)
        {
            sections.Add(Retain(assetList, AssetList.AssetName, (writer, _) => assetList.WriteTo(writer)));
        }

        if (_mapFile.BlendTileData is { } blendTileData)
        {
            sections.Add(Retain(blendTileData, BlendTileData.AssetName, (writer, _) => blendTileData.WriteTo(writer)));
        }

        if (_mapFile.WorldInfo is { } worldInfo)
        {
            sections.Add(Retain(worldInfo, WorldInfo.AssetName, (writer, names) => worldInfo.WriteTo(writer, names)));
        }

        if (_mapFile.MPPositionList is { } mpPositionList)
        {
            sections.Add(Retain(mpPositionList, MPPositionList.AssetName, (writer, names) => mpPositionList.WriteTo(writer, names)));
        }

        if (_mapFile.SidesList is { } sidesList && IsSupportedSidesListVersion(sidesList.Version))
        {
            sections.Add(Retain(sidesList, SidesList.AssetName, (writer, names) => sidesList.WriteTo(writer, names, mapHasAssetList)));
        }

        if (_mapFile.LibraryMapLists is { } libraryMapLists)
        {
            sections.Add(Retain(libraryMapLists, LibraryMapLists.AssetName, (writer, names) => libraryMapLists.WriteTo(writer, names)));
        }

        if (_mapFile.Teams is { } teams)
        {
            sections.Add(Retain(teams, Teams.AssetName, (writer, names) => teams.WriteTo(writer, names)));
        }

        if (_mapFile.PlayerScriptsList is { } playerScriptsList)
        {
            sections.Add(Retain(playerScriptsList, PlayerScriptsList.AssetName, (writer, names) => playerScriptsList.WriteTo(writer, names)));
        }

        if (_mapFile.BuildLists is { } buildLists)
        {
            sections.Add(Retain(buildLists, BuildLists.AssetName, (writer, names) => buildLists.WriteTo(writer, names, mapHasAssetList)));
        }

        if (_mapFile.PolygonTriggers is { } polygonTriggers)
        {
            sections.Add(Retain(polygonTriggers, PolygonTriggers.AssetName, (writer, _) => polygonTriggers.WriteTo(writer)));
        }

        if (_mapFile.TriggerAreas is { } triggerAreas)
        {
            sections.Add(Retain(triggerAreas, TriggerAreas.AssetName, (writer, _) => triggerAreas.WriteTo(writer)));
        }

        if (_mapFile.GlobalWaterSettings is { } globalWaterSettings)
        {
            sections.Add(Retain(globalWaterSettings, GlobalWaterSettings.AssetName, (writer, _) => globalWaterSettings.WriteTo(writer)));
        }

        if (_mapFile.FogSettings is { } fogSettings)
        {
            sections.Add(Retain(fogSettings, FogSettings.AssetName, (writer, _) => fogSettings.WriteTo(writer)));
        }

        if (_mapFile.MissionHotSpots is { } missionHotSpots)
        {
            sections.Add(Retain(missionHotSpots, MissionHotSpots.AssetName, (writer, _) => missionHotSpots.WriteTo(writer)));
        }

        if (_mapFile.MissionObjectives is { } missionObjectives)
        {
            sections.Add(Retain(missionObjectives, MissionObjectives.AssetName, (writer, _) => missionObjectives.WriteTo(writer)));
        }

        if (_mapFile.StandingWaterAreas is { } standingWaterAreas)
        {
            sections.Add(Retain(standingWaterAreas, StandingWaterAreas.AssetName, (writer, _) => standingWaterAreas.WriteTo(writer)));
        }

        if (_mapFile.RiverAreas is { } riverAreas)
        {
            sections.Add(Retain(riverAreas, RiverAreas.AssetName, (writer, _) => riverAreas.WriteTo(writer)));
        }

        if (_mapFile.StandingWaveAreas is { } standingWaveAreas)
        {
            sections.Add(Retain(standingWaveAreas, StandingWaveAreas.AssetName, (writer, _) => standingWaveAreas.WriteTo(writer)));
        }

        if (_mapFile.GlobalLighting is { } globalLighting)
        {
            sections.Add(Retain(globalLighting, GlobalLighting.AssetName, (writer, _) => globalLighting.WriteTo(writer)));
        }

        if (_mapFile.PostEffectsChunk is { } postEffectsChunk)
        {
            sections.Add(Retain(postEffectsChunk, PostEffectsChunk.AssetName, (writer, _) => postEffectsChunk.WriteTo(writer)));
        }

        if (_mapFile.EnvironmentData is { } environmentData)
        {
            sections.Add(Retain(environmentData, EnvironmentData.AssetName, (writer, _) => environmentData.WriteTo(writer)));
        }

        if (_mapFile.NamedCameras is { } namedCameras)
        {
            sections.Add(Retain(namedCameras, NamedCameras.AssetName, (writer, _) => namedCameras.WriteTo(writer)));
        }

        if (_mapFile.CameraAnimationList is { } cameraAnimationList)
        {
            sections.Add(Retain(cameraAnimationList, CameraAnimationList.AssetName, (writer, _) => cameraAnimationList.WriteTo(writer)));
        }

        if (_mapFile.CastleTemplates is { } castleTemplates)
        {
            sections.Add(Retain(castleTemplates, CastleTemplates.AssetName, (writer, names) => castleTemplates.WriteTo(writer, names)));
        }

        if (_mapFile.WaypointsList is { } waypointsList)
        {
            sections.Add(Retain(waypointsList, WaypointsList.AssetName, (writer, _) => waypointsList.WriteTo(writer)));
        }

        if (_mapFile.SkyboxSettings is { } skyboxSettings)
        {
            sections.Add(Retain(skyboxSettings, SkyboxSettings.AssetName, (writer, _) => skyboxSettings.WriteTo(writer)));
        }

        return sections;
    }

    /// <summary>Serializes one section exactly as the legacy writer frames it: version, back-patched size, payload.</summary>
    private static RetainedSection Retain(Asset section, string sectionName, Action<BinaryWriter, AssetNameCollection> writeTo)
    {
        using var stream = new MemoryStream();

        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            // A fresh name table per section: nested name indices are container level in the legacy
            // format and this section's bytes carry only the payload side of that boundary.
            writeTo(writer, new AssetNameCollection());
        }

        var bytes = stream.ToArray();

        return new RetainedSection(sectionName, section.Version, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
