#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.Maps;
using Panopticon.Data.Map;
using Panopticon.FileFormats;
using Panopticon.Logic.Map;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Backend readers that parse retained section bytes into live native-tree section objects
/// using the existing legacy section parsers without writing new parsers.
/// </summary>
internal static class MapBackendReaders
{
    public static SidesList ReadSidesList(RetainedSection section, bool mapHasAssetList)
    {
        ArgumentNullException.ThrowIfNull(section);
        var bytes = section.Bytes.ToArray();
        var names = ReconstructSidesListNames(bytes, mapHasAssetList);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(names);
        return SidesList.Parse(reader, context, mapHasAssetList);
    }

    public static Teams ReadTeams(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var bytes = section.Bytes.ToArray();
        var names = ReconstructTeamsNames(bytes);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(names);
        return Teams.Parse(reader, context);
    }

    public static PlayerScriptsList ReadPlayerScriptsList(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var bytes = section.Bytes.ToArray();
        var names = new Dictionary<uint, string>();
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
        {
            ScanPlayerScriptsList(reader, names);
        }

        using var parseStream = new MemoryStream(bytes);
        using var parseReader = new BinaryReader(parseStream);
        var context = new MapParseContext(BuildCollectionFromAssigned(names));
        return PlayerScriptsList.Parse(parseReader, context);
    }

    public static TriggerAreas ReadTriggerAreas(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return TriggerAreas.Parse(reader, context);
    }

    public static BuildLists ReadBuildLists(RetainedSection section, bool mapHasAssetList)
    {
        ArgumentNullException.ThrowIfNull(section);
        var names = new Dictionary<uint, string> { [1] = "BuildList" };
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(BuildCollectionFromAssigned(names));
        return BuildLists.Parse(reader, context, mapHasAssetList);
    }

    public static LibraryMapLists ReadLibraryMapLists(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var names = new Dictionary<uint, string> { [1] = "LibraryMaps" };
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(BuildCollectionFromAssigned(names));
        return LibraryMapLists.Parse(reader, context);
    }

    public static WorldInfo ReadWorldInfo(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var bytes = section.Bytes.ToArray();
        var names = ReconstructWorldInfoNames(bytes);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(names);
        return WorldInfo.Parse(reader, context);
    }

    public static AssetList ReadAssetList(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return AssetList.Parse(reader, context);
    }

    public static MPPositionList ReadMPPositionList(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var names = new Dictionary<uint, string> { [1] = "MPPositionInfo" };
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(BuildCollectionFromAssigned(names));
        return MPPositionList.Parse(reader, context);
    }

    public static FogSettings ReadFogSettings(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return FogSettings.Parse(reader, context);
    }

    public static PostEffectsChunk ReadPostEffectsChunk(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return PostEffectsChunk.Parse(reader, context);
    }

    public static EnvironmentData ReadEnvironmentData(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return EnvironmentData.Parse(reader, context);
    }

    public static SkyboxSettings ReadSkyboxSettings(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return SkyboxSettings.Parse(reader, context);
    }

    public static MissionHotSpots ReadMissionHotSpots(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return MissionHotSpots.Parse(reader, context);
    }

    public static MissionObjectives ReadMissionObjectives(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return MissionObjectives.Parse(reader, context);
    }

    public static CameraAnimationList ReadCameraAnimationList(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return CameraAnimationList.Parse(reader, context);
    }

    public static CastleTemplates ReadCastleTemplates(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var names = new Dictionary<uint, string> { [1] = "CastleTemplates" };
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(BuildCollectionFromAssigned(names));
        return CastleTemplates.Parse(reader, context);
    }

    // Materializers/readers for the 9 structural sections when present as retained bytes:
    public static BlendTileData ReadRetainedBlendTileData(RetainedSection section, HeightMapData heightMapData)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(heightMapData);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return BlendTileData.Parse(reader, context, heightMapData);
    }

    public static GlobalLighting ReadRetainedGlobalLighting(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return GlobalLighting.Parse(reader, context);
    }

    public static NamedCameras ReadRetainedNamedCameras(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return NamedCameras.Parse(reader, context);
    }

    public static StandingWaterAreas ReadRetainedStandingWaterAreas(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return StandingWaterAreas.Parse(reader, context);
    }

    public static RiverAreas ReadRetainedRiverAreas(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return RiverAreas.Parse(reader, context);
    }

    public static StandingWaveAreas ReadRetainedStandingWaveAreas(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return StandingWaveAreas.Parse(reader, context);
    }

    public static GlobalWaterSettings ReadRetainedGlobalWaterSettings(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return GlobalWaterSettings.Parse(reader, context);
    }

    public static PolygonTriggers ReadRetainedPolygonTriggers(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        var dataSize = reader.ReadUInt32();
        var numTriggers = reader.ReadUInt32();
        var triggers = new List<PolygonTrigger>((int)numTriggers);
        var parseVersion = version < 4 ? (ushort)4 : version;

        for (var i = 0; i < numTriggers; i++)
        {
            var area = PolygonTrigger.Parse(reader, parseVersion);
            if (area != null)
            {
                triggers.Add(area);
            }
        }

        return new PolygonTriggers
        {
            Version = version,
            Triggers = triggers,
        };
    }

    public static WaypointsList ReadRetainedWaypointsList(RetainedSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        using var stream = new MemoryStream(section.Bytes.ToArray());
        using var reader = new BinaryReader(stream);
        var context = new MapParseContext(new AssetNameCollection());
        return WaypointsList.Parse(reader, context);
    }

    private static AssetNameCollection ReconstructSidesListNames(byte[] payload, bool mapHasAssetList)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        var dataSize = reader.ReadUInt32();
        var endPos = stream.Position + dataSize;

        if (version >= 6)
        {
            reader.ReadBooleanChecked();
        }

        var numPlayers = reader.ReadInt32();
        var assigned = new Dictionary<uint, string>();

        string[] playerCandidateOrder =
        [
            "playerName",
            "playerIsHuman",
            "playerIsSkirmish",
            "playerFaction",
            "playerDisplayName",
            "playerEnemies",
            "playerAllies",
            "playerStartMoney",
            "playerColor",
            "playerNightColor",
            "multiplayerStartIndex",
            "skirmishDifficulty",
            "multiplayerIsLocal",
            "playerIsPreorder",
        ];

        for (var p = 0; p < numPlayers; p++)
        {
            var numProps = reader.ReadUInt16();
            var candidateIdx = 0;
            for (var i = 0; i < numProps; i++)
            {
                var propType = (AssetPropertyType)reader.ReadByte();
                var propIdx = reader.ReadUInt24();

                if (!assigned.ContainsKey(propIdx))
                {
                    while (candidateIdx < playerCandidateOrder.Length)
                    {
                        var candidate = playerCandidateOrder[candidateIdx++];
                        assigned[propIdx] = candidate;
                        break;
                    }
                }

                SkipPropertyValue(reader, propType);
            }

            var numBuildListItems = reader.ReadUInt32();
            for (var b = 0; b < numBuildListItems; b++)
            {
                reader.ReadInt32();
                reader.ReadVector3();
                reader.ReadSingle();
                reader.ReadUInt16PrefixedAsciiString();
                if (mapHasAssetList)
                {
                    reader.ReadBooleanChecked();
                }
            }
        }

        if (version < 5)
        {
            var numTeams = reader.ReadInt32();
            ScanTeamsProperties(reader, numTeams, assigned);

            if (stream.Position < endPos)
            {
                var scriptsAssetIdx = reader.ReadUInt32();
                assigned[scriptsAssetIdx] = "PlayerScriptsList";
                ScanPlayerScriptsList(reader, assigned);
            }
        }

        return BuildCollectionFromAssigned(assigned);
    }

    private static AssetNameCollection ReconstructTeamsNames(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        var dataSize = reader.ReadUInt32();
        var numTeams = reader.ReadInt32();

        var assigned = new Dictionary<uint, string>();
        ScanTeamsProperties(reader, numTeams, assigned);
        return BuildCollectionFromAssigned(assigned);
    }

    private static void ScanTeamsProperties(BinaryReader reader, int numTeams, Dictionary<uint, string> assigned)
    {
        string[] teamCandidateOrder =
        [
            "teamName",
            "teamOwner",
            "teamIsSingleton",
            "teamHome",
            "teamUnitType1",
            "teamUnitMinCount1",
            "teamUnitMaxCount1",
            "teamUnitType2",
            "teamUnitMinCount2",
            "teamUnitMaxCount2",
            "teamUnitType3",
            "teamUnitMinCount3",
            "teamUnitMaxCount3",
            "teamUnitType4",
            "teamUnitMinCount4",
            "teamUnitMaxCount4",
            "teamUnitType5",
            "teamUnitMinCount5",
            "teamUnitMaxCount5",
            "teamUnitType6",
            "teamUnitMinCount6",
            "teamUnitMaxCount6",
            "teamUnitType7",
            "teamUnitMinCount7",
            "teamUnitMaxCount7",
            "teamOnCreateScript",
            "teamOnIdleScript",
            "teamInitialIdleFrames",
            "teamOnUnitDestroyedScript",
            "teamOnDestroyedScript",
            "teamDestroyedThreshold",
            "teamEnemySightedScript",
            "teamAllClearScript",
            "teamAutoReinforce",
            "teamIsAIRecruitable",
            "teamIsBaseDefense",
            "teamIsPerimeterDefense",
            "teamAggressiveness",
            "teamTransportsReturn",
            "teamAvoidThreats",
            "teamAttackCommonTarget",
            "teamMaxInstances",
            "teamDescription",
            "teamProductionCondition",
            "teamProductionPriority",
            "teamProductionPrioritySuccessIncrease",
            "teamProductionPriorityFailureDecrease",
            "teamTransport",
            "teamReinforcementOrigin",
            "teamStartsFull",
            "teamTransportsExit",
            "teamVeterancy",
            "teamExecutesActionsOnCreate",
            "teamGenericScriptHook",
        ];

        for (var t = 0; t < numTeams; t++)
        {
            var numProps = reader.ReadUInt16();
            var candidateIdx = 0;
            for (var i = 0; i < numProps; i++)
            {
                var propType = (AssetPropertyType)reader.ReadByte();
                var propIdx = reader.ReadUInt24();

                if (!assigned.ContainsKey(propIdx))
                {
                    while (candidateIdx < teamCandidateOrder.Length)
                    {
                        var candidate = teamCandidateOrder[candidateIdx++];
                        assigned[propIdx] = candidate;
                        break;
                    }
                }

                SkipPropertyValue(reader, propType);
            }
        }
    }

    private static void ScanPlayerScriptsList(BinaryReader reader, Dictionary<uint, string> assigned)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
        {
            return;
        }

        var scriptsVersion = reader.ReadUInt16();
        var scriptsDataSize = reader.ReadUInt32();
        var scriptsEnd = reader.BaseStream.Position + scriptsDataSize;

        while (reader.BaseStream.Position < scriptsEnd)
        {
            var listAssetIdx = reader.ReadUInt32();
            assigned[listAssetIdx] = "ScriptList";
            ScanScriptList(reader, assigned);
        }
    }

    private static void ScanScriptList(BinaryReader reader, Dictionary<uint, string> assigned)
    {
        var version = reader.ReadUInt16();
        var dataSize = reader.ReadUInt32();
        var endPos = reader.BaseStream.Position + dataSize;

        reader.ReadUInt16PrefixedUnicodeString();

        while (reader.BaseStream.Position < endPos)
        {
            var childAssetIdx = reader.ReadUInt32();
            // Default to Script if unknown
            assigned.TryAdd(childAssetIdx, "Script");
            var childVersion = reader.ReadUInt16();
            var childSize = reader.ReadUInt32();
            reader.BaseStream.Position += childSize;
        }
    }

    private static AssetNameCollection ReconstructWorldInfoNames(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        var dataSize = reader.ReadUInt32();
        var numProps = reader.ReadUInt16();

        var assigned = new Dictionary<uint, string>();
        var intCount = 0;

        for (var i = 0; i < numProps; i++)
        {
            var propType = (AssetPropertyType)reader.ReadByte();
            var propIdx = reader.ReadUInt24();

            if (!assigned.ContainsKey(propIdx))
            {
                if (propType == AssetPropertyType.AsciiString)
                {
                    assigned[propIdx] = "mapName";
                }
                else if (propType == AssetPropertyType.Integer)
                {
                    assigned[propIdx] = intCount == 0 ? "weather" : "compression";
                    intCount++;
                }
            }

            SkipPropertyValue(reader, propType);
        }

        return BuildCollectionFromAssigned(assigned);
    }

    private static AssetNameCollection BuildCollectionFromAssigned(Dictionary<uint, string> assigned)
    {
        var maxIndex = assigned.Count > 0 ? assigned.Keys.Max() : 0u;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(maxIndex);
            for (var i = maxIndex; i >= 1; i--)
            {
                var name = assigned.TryGetValue(i, out var val) ? val : $"Unknown_{i}";
                writer.Write(name);
                writer.Write(i);
            }
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        return AssetNameCollection.Parse(reader);
    }

    private static void SkipPropertyValue(BinaryReader reader, AssetPropertyType propType)
    {
        switch (propType)
        {
            case AssetPropertyType.Boolean:
                reader.ReadBooleanChecked();
                break;
            case AssetPropertyType.Integer:
                reader.ReadInt32();
                break;
            case AssetPropertyType.RealNumber:
                reader.ReadSingle();
                break;
            case AssetPropertyType.AsciiString:
            case AssetPropertyType.Unknown:
                reader.ReadUInt16PrefixedAsciiString();
                break;
            case AssetPropertyType.UnicodeString:
                reader.ReadUInt16PrefixedUnicodeString();
                break;
            default:
                throw new InvalidDataException($"Unexpected property type {propType}.");
        }
    }
}
