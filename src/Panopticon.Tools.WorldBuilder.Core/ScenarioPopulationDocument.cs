#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Card WI-A5-30: the scenario-population companion document for a WorldBuilder map. Terrain and
/// object edits stay in <see cref="EditorMap"/>; initial-entity population edits live beside the
/// map as a declarative <c>&lt;mapPath&gt;.scenario.json</c> artifact (same companion pattern as
/// the .ini/.str files from WI-A5-34) and are routed exclusively through the shared
/// <see cref="InitialPopulationEditor"/> from WI-A5-14, so WorldBuilder and non-spatial editors
/// mutate populations through one controller. World coordinates map onto the map's playable
/// rectangle (border tiles excluded, 10 world units per tile) and convert to normalized
/// positions for the artifact.
/// </summary>
public sealed class ScenarioPopulationDocument
{
    public const string CompanionExtension = ".scenario.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly EditorMap _map;
    private readonly float _minWorldX;
    private readonly float _worldSpanX;
    private readonly float _minWorldY;
    private readonly float _worldSpanY;

    public ScenarioPopulationDocument(EditorMap map, ReplayScenario scenario)
    {
        _map = map;
        Scenario = scenario;
        Editor = new InitialPopulationEditor(scenario);

        // WI-A5-35 established the playable rectangle: border tiles excluded, horizontal scale
        // 10 world units per tile, elevation grid dimensions from HeightMapData.
        var heightMap = map.MapFile.HeightMapData;
        var border = heightMap.BorderWidth;
        _minWorldX = border * Panopticon.Terrain.HeightMap.HorizontalScale;
        _minWorldY = border * Panopticon.Terrain.HeightMap.HorizontalScale;
        _worldSpanX = (heightMap.Width - 2 * border) * Panopticon.Terrain.HeightMap.HorizontalScale;
        _worldSpanY = (heightMap.Height - 2 * border) * Panopticon.Terrain.HeightMap.HorizontalScale;
    }

    public ReplayScenario Scenario { get; }

    public InitialPopulationEditor Editor { get; }

    public bool IsDirty { get; private set; }

    public IReadOnlyList<string> LastDiagnostics => Editor.LastDiagnostics;

    public static string CompanionPath(string mapPath) => mapPath + CompanionExtension;

    /// <summary>Opens the companion document for a map, scaffolding a valid two-player scenario when none exists.</summary>
    public static ScenarioPopulationDocument Open(EditorMap map)
    {
        var path = CompanionPath(map.FilePath);
        ReplayScenario scenario;
        if (File.Exists(path))
        {
            scenario = JsonSerializer.Deserialize<ReplayScenario>(File.ReadAllText(path), ReadOptions)
                ?? CreateDefault(map);
        }
        else
        {
            scenario = CreateDefault(map);
        }

        return new ScenarioPopulationDocument(map, scenario);
    }

    public void Save()
    {
        File.WriteAllText(CompanionPath(_map.FilePath), JsonSerializer.Serialize(Scenario, WriteOptions));
        IsDirty = false;
    }

    public NormalizedPosition WorldToNormalized(float worldX, float worldY)
    {
        return new NormalizedPosition(
            Clamp01((worldX - _minWorldX) / _worldSpanX),
            Clamp01((worldY - _minWorldY) / _worldSpanY));
    }

    public (float WorldX, float WorldY) NormalizedToWorld(NormalizedPosition position)
    {
        return (
            _minWorldX + position.NormX * _worldSpanX,
            _minWorldY + position.NormY * _worldSpanY);
    }

    public bool AddAtWorld(string definitionName, int playerIndex, float worldX, float worldY, int quantity = 1)
    {
        return MarkDirty(Editor.Add(definitionName, playerIndex, WorldToNormalized(worldX, worldY), quantity));
    }

    public bool MoveSelectedToWorld(float worldX, float worldY)
    {
        return MarkDirty(Editor.MoveSelected(WorldToNormalized(worldX, worldY)));
    }

    public bool ChangeOwnerSelected(int newPlayerIndex)
    {
        return MarkDirty(Editor.ChangeOwnerSelected(newPlayerIndex));
    }

    public bool DeleteSelected()
    {
        return MarkDirty(Editor.DeleteSelected());
    }

    public void Select(int index)
    {
        Editor.Select(index);
    }

    private bool MarkDirty(bool succeeded)
    {
        if (succeeded)
        {
            IsDirty = true;
        }

        return succeeded;
    }

    private static ReplayScenario CreateDefault(EditorMap map)
    {
        return new ReplayScenario
        {
            // A factory-fresh map has no file path yet; keep the scaffold structurally valid so
            // edits validate before the first save (the path is retargeted on reload).
            MapPath = map.FilePath ?? "untitled.map",
            Players =
            [
                new PlayerSlotConfig { Name = "Player 1", FactionIndex = 2 },
                new PlayerSlotConfig { Name = "Player 2", FactionIndex = 4 },
            ],
        };
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}
