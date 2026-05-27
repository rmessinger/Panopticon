using System;
using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model;

public sealed class ActionEntry
{
    /// <summary>
    /// Unique identifier for this action, used by <see cref="AfterActionTiming.TriggerActionId"/>.
    /// Auto-generated on construction; preserved in JSON for stable cross-action references.
    /// </summary>
    public string Id { get; }

    public string Label { get; set; }
    public ActionType Type { get; set; }

    /// <summary>
    /// Object definition name for <see cref="ActionType.Construct"/> actions
    /// (e.g. <c>"AmericaBarracks"</c>, <c>"GLABarracks"</c>).
    /// Null for all other action types.
    /// </summary>
    public string? BuildingDefinitionName { get; set; }

    /// <summary>
    /// Object definition name for <see cref="ActionType.RecruitBasicUnit"/> actions
    /// (e.g. <c>"AmericaInfantryRanger"</c>, <c>"GLAInfantryRebel"</c>).
    /// Null for all other action types.
    /// </summary>
    public string? UnitDefinitionName { get; set; }

    /// <summary>
    /// Null for actions that do not target a map position (e.g. <see cref="ActionType.RecruitBasicUnit"/>).
    /// </summary>
    public PositionSpec? Position { get; set; }

    public TimingConfig Timing { get; set; }

    [JsonConstructor]
    public ActionEntry(string label, ActionType type, PositionSpec? position, TimingConfig timing,
        string? id = null, string? buildingDefinitionName = null, string? unitDefinitionName = null)
    {
        Id = id ?? GenerateId();
        Label = label;
        Type = type;
        Position = position;
        Timing = timing;
        BuildingDefinitionName = buildingDefinitionName;
        UnitDefinitionName = unitDefinitionName;
    }

    private static string GenerateId() => Guid.NewGuid().ToString("N")[..6];
}
